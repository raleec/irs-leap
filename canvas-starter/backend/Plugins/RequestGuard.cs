using System;
using Microsoft.Xrm.Sdk;

namespace IrsLeap.Plugins
{
    public sealed class RequestGuard : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var runtime = new Runtime(serviceProvider);
            runtime.Run(() =>
            {
                if (string.Equals(runtime.Context.MessageName, "Assign", StringComparison.OrdinalIgnoreCase))
                {
                    runtime.RequireRegistration("Assign", 20);
                    object value;
                    var reference = runtime.Context.InputParameters.TryGetValue("Target", out value)
                        ? value as EntityReference : null;
                    Rules.Require(reference != null && reference.LogicalName == Schema.Table,
                        "IRS LEAP requires a leap_request target.");
                    throw new InvalidPluginExecutionException("LEAP requests cannot be reassigned.");
                }
                var creating = string.Equals(runtime.Context.MessageName, "Create", StringComparison.OrdinalIgnoreCase);
                runtime.RequireRegistration(creating ? "Create" : "Update", 20);
                var target = runtime.Target();
                if (creating)
                    Create(runtime, target);
                else
                    Update(runtime, target);
            });
        }

        private static void Create(Runtime runtime, Entity target)
        {
            foreach (var field in target.Attributes.Keys)
                Rules.Require(Schema.CreateFields.Contains(field), "The field " + field + " cannot be set on a LEAP request.");
            foreach (var field in Schema.ServerFields)
                Rules.Require(!target.Contains(field) || target[field] == null,
                    field + " is managed by IRS LEAP and cannot be supplied on creation.");
            if (target.Contains(Schema.Status) && target[Schema.Status] != null)
                Rules.Require(Rules.Required<OptionSetValue>(target, Schema.Status).Value == Schema.Pending,
                    "New requests must be Pending.");
            var requester = runtime.Context.InitiatingUserId;
            Rules.EnabledInternalUser(runtime.System, requester);
            Rules.Content(target);
            Rules.Approvers(target, requester, runtime.System);
            target[Schema.Requester] = new EntityReference("systemuser", requester);
            target[Schema.Owner] = new EntityReference("systemuser", requester);
            target["owningbusinessunit"] = runtime.System.Retrieve("systemuser", requester,
                new Microsoft.Xrm.Sdk.Query.ColumnSet("businessunitid"))
                .GetAttributeValue<EntityReference>("businessunitid");
            target[Schema.Status] = new OptionSetValue(Schema.Pending);
            target["statecode"] = new OptionSetValue(0);
            target["statuscode"] = new OptionSetValue(1);
            foreach (var field in Schema.ServerFields)
                target[field] = null;
            target[Schema.Year] = ((DateTime)target[Schema.LeaveDate]).Year;
        }

        private static void Update(Runtime runtime, Entity target)
        {
            // Attribute presence is intentional: even same-value content/owner writes are forbidden.
            foreach (var field in target.Attributes.Keys)
            {
                Rules.Require(Schema.UpdateFields.Contains(field),
                    "LEAP request content, ownership and server-managed fields are immutable: " + field + ".");
                if (field == Schema.Id)
                    Rules.Require(target[field] is Guid && (Guid)target[field] == target.Id,
                        "The request ID is immutable.");
            }
            Rules.Require(Rules.TrustedWorkflowCaller(runtime.System, runtime.Context.InitiatingUserId),
                "Only IRS LEAP Automation or System Administrator may update request workflow fields.");
            Entity before;
            Rules.Require(runtime.Context.PreEntityImages.TryGetValue("Before", out before)
                && before.LogicalName == Schema.Table && before.Id == target.Id,
                "The RequestGuard Update step requires a complete pre-image named Before.");
            foreach (var field in new[]
            {
                Schema.Name, Schema.LeaveDate, Schema.StartMinute, Schema.EndMinute, Schema.Category,
                Schema.Status, Schema.Year, Schema.Requester, Schema.Owner, Schema.Approver1
            })
                Rules.Require(before.Contains(field) && before[field] != null,
                    "The Before pre-image is missing required column " + field + ".");
            Rules.Require(Rules.Required<OptionSetValue>(before, Schema.Status).Value == Schema.Pending,
                "Finalized LEAP requests cannot be changed.");

            var status = target.Contains(Schema.Status)
                ? Rules.Required<OptionSetValue>(target, Schema.Status).Value : Schema.Pending;
            Rules.Require(status >= Schema.Pending && status <= Schema.Expired, "Invalid LEAP status.");
            Rules.OptionalText(target, Schema.DecisionComments, 1000);
            Rules.OptionalText(target, Schema.ApprovalId, 100);
            Rules.OptionalText(target, Schema.ProcessingNote, 500);
            Rules.Require(status == Schema.Pending
                || (!target.Contains(Schema.ApprovalId) && !target.Contains(Schema.ProcessingNote)),
                "Approval IDs and processing notes can only be written while the request remains Pending.");
            if (status == Schema.Approved || status == Schema.Denied)
            {
                var decider = Rules.UserReference(target, Schema.DecidedBy, true);
                var selected = false;
                foreach (var field in Schema.Approvers)
                {
                    var approver = Rules.UserReference(before, field, field == Schema.Approver1);
                    selected |= approver != null && approver.Id == decider.Id;
                }
                Rules.Require(selected, "The decision must be attributed to one of this request's selected approvers.");
                Rules.EnabledInternalUser(runtime.System, decider.Id);
                target[Schema.DecidedOn] = DateTime.UtcNow;
            }
            else
            {
                Rules.Require(!target.Contains(Schema.DecidedBy) && !target.Contains(Schema.DecisionComments),
                    "Decision attribution and comments require a Pending-to-Approved or Pending-to-Denied transition.");
                if (status == Schema.Expired)
                {
                    target[Schema.DecidedBy] = null;
                    target[Schema.DecidedOn] = null;
                    target[Schema.DecisionComments] = null;
                }
            }
        }
    }
}
