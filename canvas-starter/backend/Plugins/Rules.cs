using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace IrsLeap.Plugins
{
    internal static class Rules
    {
        internal static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidPluginExecutionException(message);
        }

        internal static T Required<T>(Entity row, string field)
        {
            object value;
            Require(row.Attributes.TryGetValue(field, out value) && value is T,
                "A valid " + field + " is required.");
            return (T)value;
        }

        internal static void OptionalText(Entity row, string field, int maximum)
        {
            object value;
            if (row.Attributes.TryGetValue(field, out value) && value != null)
                Require(value is string && ((string)value).Length <= maximum,
                    field + " must be text of at most " + maximum + " characters.");
        }

        internal static EntityReference UserReference(Entity row, string field, bool required)
        {
            object value;
            if (!row.Attributes.TryGetValue(field, out value) || value == null)
            {
                Require(!required, field + " is required.");
                return null;
            }
            var reference = value as EntityReference;
            Require(reference != null && reference.LogicalName == "systemuser" && reference.Id != Guid.Empty,
                field + " must reference a provisioned Dataverse systemuser.");
            return reference;
        }

        internal static void EnabledInternalUser(IOrganizationService service, Guid id)
        {
            var user = service.Retrieve("systemuser", id,
                new ColumnSet("isdisabled", "applicationid", "accessmode", "domainname", "azureactivedirectoryobjectid"));
            var mode = Required<OptionSetValue>(user, "accessmode").Value;
            var domain = user.GetAttributeValue<string>("domainname");
            Require(!Required<bool>(user, "isdisabled")
                && user.GetAttributeValue<Guid?>("applicationid").GetValueOrDefault() == Guid.Empty
                && user.GetAttributeValue<Guid?>("azureactivedirectoryobjectid").GetValueOrDefault() != Guid.Empty
                && (mode == 0 || mode == 1 || mode == 2)
                && !string.IsNullOrWhiteSpace(domain)
                && domain.IndexOf("#EXT#", StringComparison.OrdinalIgnoreCase) < 0,
                "Requesters and approvers must be enabled, interactive internal Dataverse users, not application or guest users.");
        }

        internal static List<EntityReference> Approvers(Entity row, Guid requester, IOrganizationService service)
        {
            var result = new List<EntityReference>();
            var seen = new HashSet<Guid>();
            foreach (var field in Schema.Approvers)
            {
                var user = UserReference(row, field, field == Schema.Approver1);
                if (user == null)
                    continue;
                Require(user.Id != requester, "Self-approval is not permitted.");
                Require(seen.Add(user.Id), "Approvers must be distinct.");
                EnabledInternalUser(service, user.Id);
                result.Add(user);
            }
            return result;
        }

        internal static void Content(Entity row)
        {
            var name = Required<string>(row, Schema.Name);
            Require(!string.IsNullOrWhiteSpace(name) && name.Length <= 120,
                "A request title of 1 to 120 characters is required.");
            row[Schema.Name] = name.Trim();
            OptionalText(row, Schema.Description, 1000);
            var date = Required<DateTime>(row, Schema.LeaveDate);
            Require(date.Year >= 1900 && date.Year <= 2100,
                "Leave date must be between calendar years 1900 and 2100.");
            // DateOnly is a wall-calendar date, not an instant. Never convert time zones here.
            row[Schema.LeaveDate] = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
            var start = Required<int>(row, Schema.StartMinute);
            var end = Required<int>(row, Schema.EndMinute);
            Require(start >= 0 && start <= 1439 && end > start,
                "Minutes must describe a positive interval with a same-day start (start 0..1439, end > start).");
            Require(start % 15 == 0 && end % 15 == 0,
                "Start and end date-times must use 15-minute increments.");
            var durationMinutes = end - start;
            Require(durationMinutes % 15 == 0,
                "The selected interval must be a whole number of 15-minute units.");
            var category = Required<OptionSetValue>(row, Schema.Category).Value;
            Require(category >= 100000000 && category <= 100000012, "Select one of the 13 LEAP categories.");
            object hours;
            Require(row.Attributes.TryGetValue(Schema.Hours, out hours) && hours is int,
                "Leave hours are required as 15-minute units.");
            var quarterUnits = (int)hours;
            Require(quarterUnits >= 0, "Leave hours cannot be negative.");
            Require(quarterUnits == durationMinutes / 15,
                "Leave hours must equal the selected start/end date-time duration in 15-minute units.");
        }

        internal static bool TrustedWorkflowCaller(IOrganizationService service, Guid caller)
        {
            var direct = RoleQuery();
            var users = direct.AddLink("systemuserroles", "roleid", "roleid");
            users.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, caller);
            if (service.RetrieveMultiple(direct).Entities.Count != 0)
                return true;
            var team = RoleQuery();
            var teamRoles = team.AddLink("teamroles", "roleid", "roleid");
            var members = teamRoles.AddLink("teammembership", "teamid", "teamid");
            members.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, caller);
            return service.RetrieveMultiple(team).Entities.Count != 0;
        }

        private static QueryExpression RoleQuery()
        {
            var query = new QueryExpression("role") { ColumnSet = new ColumnSet(false), TopCount = 1 };
            query.Criteria.AddCondition("name", ConditionOperator.In,
                "IRS LEAP Automation", "System Administrator");
            return query;
        }
    }
}
