﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using TalonAdmin.Controllers.Api;
using OfficeOpenXml;
using TalonAdmin.ActionResults;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Data;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Data.Entity;
using TalonAdmin.Extensions;
using Microsoft.AspNet.Identity;
using TalonAdmin.Models.Vouchers;
using System.Web.Hosting;
using Newtonsoft.Json;

namespace TalonAdmin.Controllers
{
    [Authorize]
    [RoutePrefix("api/Excel")]
    public class ExcelController : ApiController
    {
        /// <summary>
        /// Simple helper method that validates whether the user trying to run an import and an export has actual permossions to do so
        /// </summary>
        private bool ValidateBeneficiaryRequest(int organizationId, int countryId)
        {
            using (var ctx = new Models.Admin.AdminContext())
            {
                var userId = this.RequestContext.Principal.Identity.GetUserId();
                if (!String.IsNullOrEmpty(userId))
                {
                    var user = ctx.Users.Where(u => u.Id == userId).FirstOrDefault();
                    if (user != null)
                    {

                        return user.Organization.Id == organizationId && user.Countries.Where(c => c.Id == countryId).Any();
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Validates the request to make sure the user trying to import the data has the permissions.
        /// </summary>
        private bool ValidateVendorRequest(int countryId)
        {
            using (var ctx = new Models.Admin.AdminContext())
            {
                var userId = this.RequestContext.Principal.Identity.GetUserId();
                if (!String.IsNullOrEmpty(userId))
                {
                    var user = ctx.Users.Where(u => u.Id == userId).FirstOrDefault();
                    if (user != null)
                    {

                        return user.Countries.Where(c => c.Id == countryId).Any();
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Web Api method that exports all beneficiaries in the database for this organization and country
        /// </summary>
        /// <param name="organizationId">Organization Id for the list of beneficiaries</param>
        /// <param name="countryId">Country of beneficiaries</param>
        /// <returns>Attachment with Excel Spreadsheet</returns>
        [Route("ExportBeneficiaries")]
        public async Task<IHttpActionResult> ExportBeneficiaries(int organizationId, int countryId)
        {
            using (var ctx = new Models.Vouchers.Context())
            {
                ctx.Configuration.LazyLoadingEnabled = false;
                ctx.Configuration.ProxyCreationEnabled = false;

                var beneficiaryQuery = await ctx.Beneficiaries
                    .Include("AdditionalData")
                    .Include("Group")
                    .Include("Location")
                    .Where(b => b.OrganizationId == organizationId && b.CountryId == countryId)
                    .ToArrayAsync();

                var jsonBeneficiaries = JToken.FromObject(beneficiaryQuery, new JsonSerializer
                {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                }) as JArray;
                foreach (var jsonBeneficiary in jsonBeneficiaries)
                {
                    jsonBeneficiary["Cycle"] = jsonBeneficiary["Group"].Type != JTokenType.Null ? jsonBeneficiary["Group"]["Name"] : "";
                    jsonBeneficiary["Location"] = jsonBeneficiary["Location"].Type != JTokenType.Null ? jsonBeneficiary["Location"]["Name"] : "";
                    jsonBeneficiary["Sex"] = jsonBeneficiary["Sex"].Type != JTokenType.Null ? (jsonBeneficiary["Sex"].ToString() == "0" ? "Male" : "Female") : "";
                }

                jsonBeneficiaries.Flatten("AdditionalDataObject", "Additional Data/");
                jsonBeneficiaries.RemoveProperties("Group", "AdditionalDataObject", "AdditionalData");

                var dataTable = jsonBeneficiaries.ToObject<DataTable>();

                if (beneficiaryQuery.Count() == 0)
                {
                    var empty = JArray.FromObject(new Beneficiary[] { new Beneficiary() });
                    empty.RemoveProperties("Group");

                    dataTable = empty.ToObject<DataTable>();
                    dataTable.Rows.Clear();
                }

                dataTable.TableName = "Beneficiaries";

                // Removing Id Columns because they are parsed later on in the import
                dataTable.Columns.RemoveSafe("AdditionalData");
                dataTable.Columns.RemoveSafe("Name");
                dataTable.Columns.RemoveSafe("GroupId");
                dataTable.Columns.RemoveSafe("LocationId");
                dataTable.Columns.RemoveSafe("OrganizationId");
                dataTable.Columns.RemoveSafe("CountryId");
                dataTable.Columns.RemoveSafe("WasWelcomeMessageSent");
                dataTable.Columns.RemoveSafe("PIN");

                return this.File(dataTable.ToExcelSpreadsheet(), "Beneficiaries.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            }
        }

        /// <summary>
        /// Web Api method that takes in an upload of a spreadsheet with a sheet called beneficiaries and extracts beneficiary data from it.
        /// </summary>
        /// <param name="organizationId">Organization Id for the list of beneficiaries</param>
        /// <param name="countryId">Country of beneficiaries</param>
        /// <returns>JSON with any import errors in details</returns>
        [Route("ImportBeneficiaries")]
        public async Task<IHttpActionResult> ImportBeneficiaries(int organizationId, int countryId)
        {
            if (!Request.Content.IsMimeMultipartContent())
            {
                return BadRequest();
            }

            dynamic response = new JObject();
            response.Errors = new JArray();

            string root = HostingEnvironment.MapPath("~/App_Data/uploads");
            var provider = new MultipartFormDataStreamProvider(root);

            var streamProvider = new MultipartFormDataStreamProvider(root);
            await Request.Content.ReadAsMultipartAsync(streamProvider);

            foreach (MultipartFileData fileData in streamProvider.FileData)
            {
                var fileBytes = File.ReadAllBytes(fileData.LocalFileName);

                // No need to keep the file lying around
                File.Delete(fileData.LocalFileName);

                Models.Admin.Country country = null;

                using (var ctx = new Models.Admin.AdminContext())
                {
                    country = await ctx.Countries.AsNoTracking().Where(c => c.Id == countryId).FirstOrDefaultAsync();
                }

                using (var ctx = new Models.Vouchers.Context())
                {
                    ctx.Configuration.LazyLoadingEnabled = false;
                    ctx.Configuration.ProxyCreationEnabled = false;

                    var beneficiaryQuery = await ctx.Beneficiaries
                        .Where(b => b.CountryId == countryId && b.OrganizationId == organizationId)
                        .ToListAsync();

                    var locationQuery = ctx.Locations.Where(l => l.CountryId == countryId);
                    var groupQuery = ctx.BeneficiaryGroups.Where(g => g.CountryId == countryId && g.OrganizationId == organizationId);

                    var package = new ExcelPackage(new MemoryStream(fileBytes));
                    var excelData = package.ExtractData();
                    package.Dispose();

                    if (excelData.ContainsKey("Beneficiaries"))
                    {
                        foreach (var jsonBeneficiary in excelData["Beneficiaries"])
                        {
                            try
                            {
                                var beneficiaryId = jsonBeneficiary.ValueIfExists<int?>("Id");
                                var groupName = jsonBeneficiary.ValueIfExists<string>("Cycle");
                                var locationName = jsonBeneficiary.ValueIfExists<string>("Location");

                                jsonBeneficiary["Sex"] = (jsonBeneficiary.ValueIfExists<string>("Sex") ?? "").ToString().Trim().ToLower() == "male" ? 0 : 1;

                                jsonBeneficiary.RemoveProperties("Name", "Cycle", "Location", "OrganizationId", "CountryId");

                                var isNew = beneficiaryId == null;
                                Models.Vouchers.Beneficiary beneficiary = null;

                                if (beneficiaryId != null)
                                    beneficiary = beneficiaryQuery.Where(o => o.Id == beneficiaryId.Value).FirstOrDefault();
                                else
                                    beneficiary = new Models.Vouchers.Beneficiary();

                                if (beneficiary == null) throw new Exception("This beneficiary belongs to another organization or another country."); // Something doesn't smell right

                                if (isNew)
                                {
                                    jsonBeneficiary["Id"] = 0;
                                    jsonBeneficiary["CountryId"] = countryId;
                                    jsonBeneficiary["OrganizationId"] = organizationId;
                                }

                                var numberRegex = new System.Text.RegularExpressions.Regex(String.Format("^(\\+{0}|{0}|0|1)", country.CountryCallingCode));
                                jsonBeneficiary["MobileNumber"] = String.Format("+{0}{1}", country.CountryCallingCode, numberRegex.Replace(jsonBeneficiary["MobileNumber"].ToString(), ""));


                                jsonBeneficiary.MergeChangesInto(beneficiary);

                                #region Assigning Group

                                BeneficiaryGroup group = null;

                                // If group is filled out, try to find it or create new one
                                if (!String.IsNullOrEmpty(groupName))
                                {
                                    group = groupQuery.Where(g => g.Name.ToLower().Trim() == groupName.Trim().ToLower()).FirstOrDefault();

                                    if (group == null)
                                    {
                                        group = new Models.Vouchers.BeneficiaryGroup
                                        {
                                            Name = groupName,
                                            OrganizationId = organizationId,
                                            CountryId = countryId
                                        };

                                        ctx.BeneficiaryGroups.Add(group);
                                    }
                                }
                                if (group != null)
                                {
                                    beneficiary.Group = group;
                                    beneficiary.GroupId = group.Id;
                                }
                                else
                                {
                                    beneficiary.GroupId = null;
                                    beneficiary.Group = null;
                                }

                                #endregion

                                #region Assigning Location
                                Location location = null;

                                // If location is filled out try to find or create new one
                                if (!String.IsNullOrEmpty(locationName))
                                {
                                    location = locationQuery.Where(l => l.Name.ToLower().Trim() == locationName.Trim().ToLower()).FirstOrDefault();

                                    if (location == null)
                                    {
                                        location = new Models.Vouchers.Location
                                        {
                                            Name = locationName,
                                            CountryId = countryId
                                        };

                                        ctx.Locations.Add(location);
                                    }
                                }

                                if (location != null)
                                {
                                    beneficiary.Location = location;
                                    beneficiary.LocationId = location.Id;
                                }
                                else
                                {
                                    beneficiary.Location = null;
                                    beneficiary.LocationId = null;
                                }

                                #endregion

                                #region Additional Data
                                var currentData = new BeneficiaryAdditionalData[0];

                                if (!isNew)
                                    currentData = await ctx.BeneficiaryAdditionalData.Where(b => b.ParentId == beneficiary.Id).ToArrayAsync();

                                var additionalData = jsonBeneficiary.Properties()
                                    .Where(p => p.Name.StartsWith("Additional Data/"))
                                    .Select(p => new BeneficiaryAdditionalData
                                        {
                                            Id = 0,
                                            ParentId = beneficiary.Id,
                                            Key = p.Name.Replace("Additional Data/", ""),
                                            Value = p.Value.ToString()
                                        })
                                    .ToList();

                                additionalData.ForEach(a =>
                                {
                                    var q = currentData.Where(o => o.Key == a.Key);
                                    if (q.Any())
                                    {
                                        var other = q.First();
                                        a.Id = other.Id;
                                        other.Value = a.Value;
                                    }
                                });

                                ctx.BeneficiaryAdditionalData.AddRange(additionalData.Where(a => a.Id == 0));

                                #endregion

                                if (isNew)
                                {
                                    ctx.Beneficiaries.Add(beneficiary);
                                }

                                await ctx.SaveChangesAsync();
                            }
                            catch (Exception e)
                            {
                                response.Errors.Add(JObject.FromObject(new
                                {
                                    ErrorText = e.Message,
                                    Line = jsonBeneficiary["__RowNumber"]
                                }));
                            }
                        }
                    }
                }
            }


            return Ok<JObject>(response);
        }

        /// <summary>
        /// Web Api method that exports all vendors in the database for this  country
        /// </summary>
        /// <param name="countryId">Country of vendors</param>
        /// <returns>Attachment with Excel Spreadsheet</returns>
        [Route("ExportVendors")]
        public async Task<IHttpActionResult> ExportVendors(int countryId)
        {
            using (var ctx = new Models.Vouchers.Context())
            {
                ctx.Configuration.LazyLoadingEnabled = false;
                ctx.Configuration.ProxyCreationEnabled = false;

                var vendorQuery = await ctx.Vendors
                    .Include("Location")
                    .Include("Type")
                    .Where(b => b.CountryId == countryId)
                    .ToArrayAsync();
                var jsonString = JsonConvert.SerializeObject(vendorQuery, Formatting.Indented,
                    new JsonSerializerSettings
                    {
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                    });

                var jsonCollection = JToken.Parse(jsonString) as JArray;

                jsonCollection.Descendants().OfType<JProperty>()
                  .Where(p => new string[] { "LocationId", "CountryId", "TypeId", "ParentRecord" }.Contains(p.Name))
                  .ToList()
                  .ForEach(att => att.Remove());

                foreach (var jsonObject in jsonCollection)
                {
                    jsonObject["Location"] = jsonObject["Location"].Type != JTokenType.Null ? jsonObject["Location"]["Name"] : "";
                    jsonObject["Type"] = jsonObject["Type"].Type != JTokenType.Null ? jsonObject["Type"]["Name"] : "";
                }
                var dataTable = jsonCollection.ToObject<DataTable>();

                if (vendorQuery.Count() == 0)
                {
                    dataTable = JToken.FromObject(new Vendor[] { new Vendor() }).ToObject<DataTable>();
                    dataTable.Rows.Clear();
                }

                // Removing Id Columns because they are parsed later on in the import

                dataTable.TableName = "Vendors";

                return this.File(dataTable.ToExcelSpreadsheet(), "Vendors.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            }
        }

        /// <summary>
        /// Web Api methods that allow uses to bulk import and update vendors in the system
        /// </summary>
        /// <param name="countryId">Country of vendors</param>
        /// <returns>JSON with any import errors in details</returns>
        [Route("ImportVendors")]
        public async Task<IHttpActionResult> ImportVendors(int countryId)
        {
            if (!Request.Content.IsMimeMultipartContent())
            {
                return BadRequest();
            }

            dynamic response = new JObject();
            response.Errors = new JArray();

            string root = HostingEnvironment.MapPath("~/App_Data/uploads");
            var provider = new MultipartFormDataStreamProvider(root);

            var streamProvider = new MultipartFormDataStreamProvider(root);
            await Request.Content.ReadAsMultipartAsync(streamProvider);

            foreach (MultipartFileData fileData in streamProvider.FileData)
            {
                var fileBytes = File.ReadAllBytes(fileData.LocalFileName);

                // No need to keep the file lying around
                File.Delete(fileData.LocalFileName);

                Models.Admin.Country country = null;

                using (var ctx = new Models.Admin.AdminContext())
                {
                    country = await ctx.Countries.AsNoTracking().Where(c => c.Id == countryId).FirstOrDefaultAsync();
                }

                using (var ctx = new Models.Vouchers.Context())
                {
                    ctx.Configuration.LazyLoadingEnabled = false;
                    ctx.Configuration.ProxyCreationEnabled = false;

                    var vendorQuery = await ctx.Vendors.Where(v => v.CountryId == countryId).ToListAsync();
                    var locationQuery = ctx.Locations.Where(l => l.CountryId == countryId);
                    var vendorTypeQuery = ctx.VendorTypes.Where(l => l.CountryId == countryId);

                    var package = new ExcelPackage(new MemoryStream(fileBytes));
                    var excelData = package.ExtractData();
                    package.Dispose();

                    if (excelData.ContainsKey("Vendors"))
                    {
                        foreach (var jsonVendor in excelData["Vendors"])
                        {
                            try
                            {
                                var vendorId = jsonVendor.ValueIfExists<int?>("Id");
                                var locationName = jsonVendor.ValueIfExists<string>("Location");
                                var typeName = jsonVendor.ValueIfExists<string>("Type");

                                jsonVendor.Remove("Location");
                                jsonVendor.Remove("Type");

                                // Trust no one
                                jsonVendor.Remove("CountryId");

                                var isNew = vendorId == null;
                                Models.Vouchers.Vendor vendor = null;

                                if (vendorId != null)
                                    vendor = vendorQuery.Where(o => o.Id == vendorId.Value).FirstOrDefault();
                                else
                                    vendor = new Models.Vouchers.Vendor();

                                if (vendor == null) throw new Exception("This vendor is already in another country."); // Something doesn't smell right

                                if (isNew)
                                {
                                    jsonVendor["Id"] = 0;
                                    jsonVendor["CountryId"] = countryId;
                                }

                                var numberRegex = new System.Text.RegularExpressions.Regex(String.Format("^(\\+{0}|{0}|0|1)", country.CountryCallingCode));
                                jsonVendor["MobileNumber"] = String.Format("+{0}{1}", country.CountryCallingCode, numberRegex.Replace(jsonVendor["MobileNumber"].ToString(), ""));


                                jsonVendor.MergeChangesInto(vendor);

                                #region Assigning Location
                                Location location = null;

                                // If location is filled out try to find or create new one
                                if (!String.IsNullOrEmpty(locationName))
                                {
                                    location = locationQuery.Where(l => l.Name.ToLower().Trim() == locationName.Trim().ToLower()).FirstOrDefault();

                                    if (location == null)
                                    {
                                        location = new Models.Vouchers.Location
                                        {
                                            Name = locationName,
                                            CountryId = countryId
                                        };

                                        ctx.Locations.Add(location);
                                    }
                                }

                                if (location != null)
                                {
                                    vendor.Location = location;
                                    vendor.LocationId = location.Id;
                                }
                                else
                                {
                                    vendor.Location = null;
                                    vendor.LocationId = null;
                                }


                                #endregion

                                #region Assigning Type
                                VendorType type = null;

                                // If location is filled out try to find or create new one
                                if (!String.IsNullOrEmpty(typeName))
                                {
                                    type = vendorTypeQuery.Where(l => l.Name.ToLower().Trim() == locationName.Trim().ToLower()).FirstOrDefault();

                                    if (type == null)
                                    {
                                        type = new Models.Vouchers.VendorType
                                        {
                                            Name = typeName,
                                            CountryId = countryId
                                        };

                                        ctx.VendorTypes.Add(type);
                                    }
                                }

                                if (type != null)
                                {
                                    vendor.Type = type;
                                    vendor.TypeId = type.Id;
                                }
                                else
                                {
                                    vendor.Type = null;
                                    vendor.TypeId = null;
                                }


                                #endregion

                                if (isNew)
                                {
                                    ctx.Vendors.Add(vendor);
                                }

                                await ctx.SaveChangesAsync();
                            }
                            catch (Exception e)
                            {
                                response.Errors.Add(JObject.FromObject(new
                                {
                                    ErrorText = e.Message,
                                    Line = jsonVendor["__RowNumber"]
                                }));
                            }
                        }
                    }

                }

            }

            return Ok<JObject>(response);
        }

        [Route("ImportMetadata")]
        public async Task<IHttpActionResult> ImportMetadata()
        {
            if (!Request.Content.IsMimeMultipartContent())
            {
                return BadRequest();
            }

            dynamic response = new JObject();
            response.Errors = new JArray();

            string root = HostingEnvironment.MapPath("~/App_Data/uploads");
            var provider = new MultipartFormDataStreamProvider(root);

            var streamProvider = new MultipartFormDataStreamProvider(root);
            await Request.Content.ReadAsMultipartAsync(streamProvider);

            foreach (MultipartFileData fileData in streamProvider.FileData)
            {
            }

            return Ok();
        }

        [AllowAnonymous]
        [Route("ExportMetadata"), HttpGet]
        public async Task<IHttpActionResult> ExportMetadata()
        {
            using (var ctx = new Models.Admin.AdminContext())
            {
                ctx.Configuration.ProxyCreationEnabled = false;
                ctx.Configuration.LazyLoadingEnabled = false;

                var dataTable = new DataTable();
                var jsonSerializer = new JsonSerializer() { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };

                var actions = JArray.FromObject(await ctx.Actions.ToArrayAsync(), jsonSerializer).ToObject<DataTable>();
                var menuCategories = JArray.FromObject(await ctx.MenuCategories.ToArrayAsync(), jsonSerializer).ToObject<DataTable>();

                var menuItems = JArray.FromObject(await ctx.MenuItems.ToArrayAsync(), jsonSerializer)
                    .RemoveProperties("Category", "Children")
                    .ToObject<DataTable>();

                var roles = JArray.FromObject(await ctx.Roles.ToArrayAsync(), jsonSerializer)
                    .RemoveProperties("Users")
                    .ToObject<DataTable>();

                var actionRoles = JArray.FromObject(await ctx.ActionRoles.ToArrayAsync(), jsonSerializer)
                    .RemoveProperties("Action", "Role")
                    .ToObject<DataTable>();

                var menuCategoryRoles = JArray.FromObject(await ctx.MenuCategoryRoles.ToArrayAsync(), jsonSerializer)
                    .RemoveProperties("Category", "Role")
                    .ToObject<DataTable>();

                actions.TableName = "Actions";
                menuCategories.TableName = "MenuCategories";
                menuItems.TableName = "MenuItems";
                roles.TableName = "Roles";
                actionRoles.TableName = "ActionRoles";
                menuCategoryRoles.TableName = "MenuCategoryRoles";

                var dataSet = new DataSet();

                dataSet.Tables.Add(actions);
                dataSet.Tables.Add(menuCategories);
                dataSet.Tables.Add(menuItems);
                dataSet.Tables.Add(roles);
                dataSet.Tables.Add(actionRoles);
                dataSet.Tables.Add(menuCategoryRoles);

                return this.File(dataSet.ToExcelSpreadsheet(), "Metadata.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            }
        }
    }
}