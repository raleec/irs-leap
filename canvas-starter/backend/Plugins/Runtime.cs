using System;
using Microsoft.Xrm.Sdk;

namespace IrsLeap.Plugins
{
    internal sealed class Runtime
    {
        internal readonly IPluginExecutionContext Context;
        internal readonly IOrganizationService System;
        private readonly ITracingService trace;

        internal Runtime(IServiceProvider provider)
        {
            Context = (IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
            trace = (ITracingService)provider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)provider.GetService(typeof(IOrganizationServiceFactory));
            // Elevation is restricted to explicit identity/role checks, grants and authorized totals.
            System = factory.CreateOrganizationService(null);
        }

        internal void Run(Action action)
        {
            try
            {
                if (Context.InitiatingUserId == Guid.Empty)
                    throw new InvalidPluginExecutionException("IRS LEAP requires an authenticated initiating user.");
                action();
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (Exception exception)
            {
                trace?.Trace("IRS LEAP {0}; correlation {1}: {2}",
                    Context.MessageName, Context.CorrelationId, exception);
                throw new InvalidPluginExecutionException(
                    "IRS LEAP could not complete the operation. Contact your Dataverse administrator.", exception);
            }
        }

        internal void RequireRegistration(string message, int stage)
        {
            if (!string.Equals(Context.MessageName, message, StringComparison.OrdinalIgnoreCase)
                || Context.Stage != stage || Context.Mode != 0)
                throw new InvalidPluginExecutionException("IRS LEAP plug-in registration is incorrect.");
        }

        internal Entity Target()
        {
            object target;
            if (!Context.InputParameters.TryGetValue("Target", out target)
                || !(target is Entity) || ((Entity)target).LogicalName != Schema.Table)
                throw new InvalidPluginExecutionException("IRS LEAP requires a leap_request target.");
            return (Entity)target;
        }
    }
}
