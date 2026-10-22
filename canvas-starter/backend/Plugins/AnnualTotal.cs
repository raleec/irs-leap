using System;
using System.Globalization;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace IrsLeap.Plugins
{
    public sealed class AnnualTotal : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var runtime = new Runtime(serviceProvider);
            runtime.Run(() =>
            {
                runtime.RequireRegistration(Schema.AnnualApi, 30);
                object requesterInput;
                object yearInput;
                Guid requester;
                Rules.Require(runtime.Context.InputParameters.TryGetValue("RequesterId", out requesterInput)
                    && requesterInput is string && Guid.TryParse((string)requesterInput, out requester)
                    && requester != Guid.Empty, "RequesterId must be a non-empty GUID string.");
                // The guarded conversion above is repeated to satisfy definite assignment on all framework compilers.
                requester = Guid.Parse((string)requesterInput);
                Rules.Require(runtime.Context.InputParameters.TryGetValue("CalendarYear", out yearInput)
                    && yearInput is int && (int)yearInput >= 1900 && (int)yearInput <= 2100,
                    "CalendarYear must be an integer from 1900 to 2100.");
                var year = (int)yearInput;
                var caller = runtime.Context.InitiatingUserId;
                Rules.Require(caller == requester || AssignedApprover(runtime.System, requester, caller),
                    "Annual totals are available only to the requester or an assigned approver on one of their saved requests.");

                // No requester profile or totals are read until the row-level authorization succeeds.
                var person = runtime.System.Retrieve("systemuser", requester, new ColumnSet("fullname"));
                var totals = Aggregate(runtime.System, requester, year);
                var approved = Integer(totals, "approvedrequests");
                var recorded = Integer(totals, "recordedhours");
                Rules.Require(recorded <= approved, "Dataverse returned an inconsistent annual aggregate.");
                runtime.Context.OutputParameters["TotalHours"] = Integer(totals, "totalhours");
                runtime.Context.OutputParameters["MissingHours"] = approved - recorded;
                runtime.Context.OutputParameters["ApprovedRequests"] = approved;
                runtime.Context.OutputParameters["RequesterName"] = person.GetAttributeValue<string>("fullname") ?? string.Empty;
            });
        }

        private static bool AssignedApprover(IOrganizationService service, Guid requester, Guid caller)
        {
            var query = new QueryExpression(Schema.Table) { ColumnSet = new ColumnSet(false), TopCount = 1 };
            query.Criteria.AddCondition(Schema.Requester, ConditionOperator.Equal, requester);
            var selected = new FilterExpression(LogicalOperator.Or);
            foreach (var field in Schema.Approvers)
                selected.AddCondition(field, ConditionOperator.Equal, caller);
            query.Criteria.AddFilter(selected);
            return service.RetrieveMultiple(query).Entities.Count != 0;
        }

        private static Entity Aggregate(IOrganizationService service, Guid requester, int year)
        {
            var fetch = string.Format(CultureInfo.InvariantCulture,
                "<fetch aggregate='true'><entity name='leap_request'>" +
                "<attribute name='leap_requestid' alias='approvedrequests' aggregate='count' />" +
                "<attribute name='leap_hours' alias='totalhours' aggregate='sum' />" +
                "<attribute name='leap_hours' alias='recordedhours' aggregate='countcolumn' />" +
                "<filter type='and'>" +
                "<condition attribute='leap_requesterid' operator='eq' value='{0:D}' />" +
                "<condition attribute='leap_year' operator='eq' value='{1}' />" +
                "<condition attribute='leap_status' operator='eq' value='100000001' />" +
                "</filter></entity></fetch>", requester, year);
            try
            {
                var rows = service.RetrieveMultiple(new FetchExpression(fetch));
                Rules.Require(rows.Entities.Count == 1, "Dataverse did not return a complete annual aggregate.");
                return rows.Entities[0];
            }
            catch (FaultException<OrganizationServiceFault> fault)
            {
                // Never page ordinary records or silently return a partial total when aggregation fails.
                throw new InvalidPluginExecutionException(
                    "Dataverse could not calculate the full annual total. Its aggregate query record limit " +
                    "(normally 50,000 matching records) or another service restriction may have been reached. " +
                    "No partial total was returned.", fault);
            }
        }

        private static int Integer(Entity aggregate, string alias)
        {
            object value;
            if (!aggregate.Attributes.TryGetValue(alias, out value) || value == null)
                return 0;
            var aliased = value as AliasedValue;
            Rules.Require(aliased != null, "Dataverse returned an invalid annual aggregate.");
            if (aliased.Value == null)
                return 0;
            var number = Convert.ToDecimal(aliased.Value, CultureInfo.InvariantCulture);
            Rules.Require(number >= 0 && number <= int.MaxValue && number == decimal.Truncate(number),
                "The annual aggregate is outside the supported integer range.");
            return checked((int)number);
        }
    }
}
