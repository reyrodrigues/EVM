﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EmergencyVoucherManagement.Models.Vouchers
{
    public class BeneficiaryDistribution : Entity
    {
        public virtual int DistributionId { get; set; }
        public virtual int BeneficiaryId { get; set; }


        public virtual Beneficiary Beneficiary { get; set; }
        public virtual Distribution Distribution { get; set; }
    }
}