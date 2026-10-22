using System;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace IrsLeap.Plugins
{
    public sealed class RequestShare : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var runtime = new Runtime(serviceProvider);
            runtime.Run(() =>
            {
                runtime.RequireRegistration("Create", 40);
                var target = runtime.Target();
                var id = runtime.Context.PrimaryEntityId != Guid.Empty ? runtime.Context.PrimaryEntityId : target.Id;
                Rules.Require(runtime.Context.IsInTransaction, "RequestShare must run synchronously inside the Create transaction.");
                var request = runtime.System.Retrieve(Schema.Table, id,
                    new ColumnSet(Schema.Requester, Schema.Owner, Schema.Approver1, Schema.Approver2, Schema.Approver3));
                var requester = Rules.UserReference(request, Schema.Requester, true);
                var owner = Rules.UserReference(request, Schema.Owner, true);
                Rules.Require(requester.Id == runtime.Context.InitiatingUserId && owner.Id == requester.Id,
                    "RequestGuard must derive the requester and owner before RequestShare runs.");
                foreach (var approver in Rules.Approvers(request, requester.Id, runtime.System))
                {
                    runtime.System.Execute(new GrantAccessRequest
                    {
                        Target = new EntityReference(Schema.Table, id),
                        PrincipalAccess = new PrincipalAccess
                        {
                            Principal = approver,
                            AccessMask = AccessRights.ReadAccess
                        }
                    });
                }
            });
        }
    }
}
