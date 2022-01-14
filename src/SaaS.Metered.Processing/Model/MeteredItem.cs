using System;
using System.Collections.Generic;
using System.Text;

namespace SaaS.Metered.Processing.Model
{
       public class MeteredItem
    {
        public string id { get; set; }
        public string SubscriptionId { get; set; }
        public string DimensionId { get; set; }

        public string PlanId { get; set; }

        public int MeterProcessStatus { get; set; }

        public DateTime CreatedDate { get; set; }
    }
}
