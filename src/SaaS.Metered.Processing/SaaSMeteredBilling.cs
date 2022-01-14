using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Cosmos;
using SaaS.Metered.Processing;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using SaaS.Metered.Processing.Model;
namespace SaaS.Metering.Processing
{
    public static class SaaSMeteredBilling
    {
        private static CosmosClient client = new CosmosClient(GetEnvironmentVariable("CosmosDb_Uri"), GetEnvironmentVariable("CosmosDb_Key"));
        [FunctionName("SaaSMeteredBilling")]
        // run every hour
        public static async Task RunAsync([TimerTrigger("0 0 * * * *",RunOnStartup =true)]TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");


            List<DistinctItem> distinctItems = new List<DistinctItem>();


            //1-  Call database and get unique subs
            var collectionId = GetEnvironmentVariable("CosmosDb_Collection");
            var databaseId = GetEnvironmentVariable("CosmosDb_Database");
            var sqlQueryText = "SELECT distinct c.SubscriptionId,c.PlanId, c.DimensionId FROM c where c.MeterProcessStatus = 0";
            log.LogInformation("Running query: {0}\n", sqlQueryText);

            QueryDefinition queryDefinition = new QueryDefinition(sqlQueryText);
            FeedIterator<DistinctItem> queryResultSetIterator = client.GetContainer(databaseId, collectionId).GetItemQueryIterator<DistinctItem>(queryDefinition);
            while (queryResultSetIterator.HasMoreResults)
            {
                FeedResponse<DistinctItem> currentResultSet = await queryResultSetIterator.ReadNextAsync();
                foreach (DistinctItem item in currentResultSet)
                {
                    distinctItems.Add(item);
                }
            }

            var subscription = distinctItems.Select(s => s.SubscriptionId).Distinct();
            foreach (string sub in subscription)
                await ProcesSubscriptionPlans(sub, distinctItems);
            
        }

        public static async Task ProcesSubscriptionPlans(string subscriptionId, List<DistinctItem> distinctItems)
        {
            // get all plans for this subscription
            var plans = distinctItems.Where(p => p.SubscriptionId == subscriptionId).ToList();

            // get unique plan
            var distinctPlan = plans.Select(p => p.PlanId).Distinct();

            foreach (string plan in distinctPlan)
                await ProcessPlanDimensions(subscriptionId, plan, distinctItems);


        }


        public static async Task ProcessPlanDimensions(string subscriptionId, string planId, List<DistinctItem> distinctItems)
        {
            // get all Dimension for this subscription and plan
            var dimensions = distinctItems.Where(p => p.SubscriptionId == subscriptionId && p.PlanId==planId).ToList();

            // get dimension 
            var distinctDimensions = dimensions.Select(d => d.DimensionId).Distinct();

            foreach (string dimension in distinctDimensions)
                await ProcessMeteredDimension(subscriptionId, planId, dimension);
        }


        public static async Task ProcessMeteredDimension(string subscriptionId, string planId, string dimensionId)
        {
            try
            {
                List<MeteredItem> items = new List<MeteredItem>();
                var collectionId = GetEnvironmentVariable("CosmosDb_Collection");
                var databaseId = GetEnvironmentVariable("CosmosDb_Database");
                var sqlQueryText = String.Format("select * FROM c where c.MeterProcessStatus = 0 and c.SubscriptionId='{0}' and c.PlanId='{1}' and c.DimensionId='{2}'", subscriptionId, planId, dimensionId);
                QueryDefinition queryDefinition = new QueryDefinition(sqlQueryText);
                FeedIterator<MeteredItem> queryResultSetIterator = client.GetContainer(databaseId, collectionId).GetItemQueryIterator<MeteredItem>(queryDefinition);
                while (queryResultSetIterator.HasMoreResults)
                {
                    FeedResponse<MeteredItem> currentResultSet = await queryResultSetIterator.ReadNextAsync();
                    foreach (MeteredItem item in currentResultSet)
                    {
                        items.Add(item);
                    }
                }

                int total = items.Count;


                // POST: https://marketplaceapi.microsoft.com/api/usageEvent?api-version=2018-08-31
                // Body
                //{
                //  "resourceId": < guid >, // unique identifier of the resource against which usage is emitted. 
                //  "quantity": 5.0, // how many units were consumed for the date and hour specified in effectiveStartTime, must be greater than 0 or a double integer
                //  "dimension": "dim1", // custom dimension identifier
                //  "effectiveStartTime": "2018-12-01T08:30:14", // time in UTC when the usage event occurred, from now and until 24 hours back
                //  "planId": "plan1", // id of the plan purchased for the offer
                // }

                // Get Auth
                string token = AuthHelper.GetTokenAsync().Result;
                //Build Body

                var billingItem = new MeteredBillingItem();
                billingItem.Dimension = dimensionId;
                billingItem.PlanId = planId;
                billingItem.ResourceId = subscriptionId;
                billingItem.Quantity = total;
                billingItem.EffectiveStartTime = DateTime.Now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss"); ;
                var json = JsonConvert.SerializeObject(billingItem);
                var data = new StringContent(json, Encoding.UTF8, "application/json");

                // Send API
                string billingUrl = "https://marketplaceapi.microsoft.com/api/usageEvent?api-version=2018-08-31";
                HttpClient httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var result = await  httpClient.PostAsync(billingUrl, data);

                

                if (result.StatusCode == System.Net.HttpStatusCode.OK || result.StatusCode == System.Net.HttpStatusCode.Created)
                {
                    // Update Cosmos
                    foreach (MeteredItem item in items)
                    {
                        item.MeterProcessStatus = 1;
                        await client.GetContainer(databaseId, collectionId).UpsertItemAsync(item);
                    }
                }
            }
            catch(Exception ex)
            {
                throw ex;
            }

        }


        public static string GetEnvironmentVariable(string variableName)
        {
            return Environment.GetEnvironmentVariable(variableName);
        }


    }





}
