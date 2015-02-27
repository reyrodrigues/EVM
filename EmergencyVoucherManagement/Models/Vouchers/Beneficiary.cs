﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EmergencyVoucherManagement.Models.Vouchers
{
    public class Beneficiary: Entity
    {
        public virtual string Name { get; set; }
        public virtual DateTime DateOfBirth { get; set; }
        public virtual string NationalId { get; set; }
        public virtual string MobileNumber { get; set; }
        public virtual short? Sex { get; set; }
        public virtual string IRCId { get; set; }

        public virtual bool? Disabled { get; set; }
        public virtual bool? WasWelcomeMessageSent { get; set; }

        public virtual string PIN { get; set; }

        public virtual int? LocationId { get; set; }
        public virtual Location Location { get; set; }

        public virtual ICollection<BeneficiaryDistribution> Distributions { get; set; }
    }
}