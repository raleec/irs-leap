using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.ServiceModel;
using System.Xml.Linq;
using IrsLeap.Plugins;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static class Program
{
    private static readonly Guid Requester = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid First = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Second = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Third = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid RowId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static int passed;
    private static int failed;

    private static int Main()
    {
        Console.WriteLine("IRS LEAP offline source validation; stub Dataverse only, no network or live UI tests.");
        ContentTests();
        IdentityTests();
        ApproverTests();
        GuardTests();
        ShareTests();
        AnnualTests();
        Console.WriteLine("RESULT: " + passed + " passed; " + failed + " failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void Test(string name, Action action)
    {
        try
        {
            action();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + error);
        }
    }

    private static void Check(bool value, string message = "Assertion failed.")
    {
        if (!value)
            throw new Exception(message);
    }

    private static void Reject(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidPluginExecutionException error)
        {
            Check(error.Message.Contains(message, StringComparison.OrdinalIgnoreCase),
                "Expected rejection containing '" + message + "', got '" + error.Message + "'.");
            return;
        }
        throw new Exception("Expected InvalidPluginExecutionException containing '" + message + "'.");
    }

    private static Entity ValidRequest()
    {
        return new Entity(Schema.Table, RowId)
        {
            [Schema.Name] = "Annual leave",
            [Schema.LeaveDate] = new DateTime(2026, 10, 21),
            [Schema.StartMinute] = 540,
            [Schema.EndMinute] = 600,
            [Schema.Category] = new OptionSetValue(100000000),
            [Schema.Approver1] = new EntityReference("systemuser", First)
        };
    }

    private static Entity Before()
    {
        var row = ValidRequest();
        row[Schema.Requester] = new EntityReference("systemuser", Requester);
        row[Schema.Owner] = new EntityReference("systemuser", Requester);
        row[Schema.Status] = new OptionSetValue(Schema.Pending);
        row[Schema.Year] = 2026;
        return row;
    }

    private static Entity User(Guid id)
    {
        return new Entity("systemuser", id)
        {
            ["isdisabled"] = false,
            ["accessmode"] = new OptionSetValue(0),
            ["domainname"] = "employee@example.invalid",
            ["azureactivedirectoryobjectid"] = id,
            ["fullname"] = "Offline Employee"
        };
    }

    private static StubService Users()
    {
        return new StubService
        {
            OnRetrieve = (name, id, columns) =>
            {
                Check(name == "systemuser", "Only user retrieval was expected.");
                return User(id);
            }
        };
    }

    private static void ContentTests()
    {
        Test("Content valid baseline", () => Rules.Content(ValidRequest()));
        foreach (var value in new object[] { null, "", " \t", new string('x', 121), 7 })
        {
            var title = value;
            Test("Content rejects invalid title " + (title ?? "<null>"), () =>
            {
                var row = ValidRequest();
                row[Schema.Name] = title;
                Reject(() => Rules.Content(row), title is string ? "title" : "leap_name");
            });
        }
        Test("Content requires title presence", () =>
        {
            var row = ValidRequest();
            row.Attributes.Remove(Schema.Name);
            Reject(() => Rules.Content(row), "leap_name");
        });
        Test("Content trims title and accepts maximum description", () =>
        {
            var row = ValidRequest();
            row[Schema.Name] = "  Leave  ";
            row[Schema.Description] = new string('x', 1000);
            Rules.Content(row);
            Check((string)row[Schema.Name] == "Leave");
            row[Schema.Name] = new string('x', 120);
            Rules.Content(row);
        });
        foreach (var value in new object[] { new string('x', 1001), 1 })
        {
            var description = value;
            Test("Content rejects description " + description.GetType().Name, () =>
            {
                var row = ValidRequest();
                row[Schema.Description] = description;
                Reject(() => Rules.Content(row), "leap_description");
            });
        }
        Test("Content accepts omitted hours", () =>
        {
            var row = ValidRequest();
            Rules.Content(row);
            Check(!row.Contains(Schema.Hours), "Omitted hours must not become zero.");
        });
        foreach (var value in new object[] { null, 0, 1, 24 })
        {
            var hours = value;
            Test("Content accepts hours " + (hours ?? "<blank>"), () =>
            {
                var row = ValidRequest();
                row[Schema.Hours] = hours;
                Rules.Content(row);
                Check(Equals(row[Schema.Hours], hours), "Hours must preserve blank versus zero.");
            });
        }
        foreach (var value in new object[] { -1, 25, 1.5m, 1m, 1.0d, "1" })
        {
            var hours = value;
            Test("Content rejects hours " + hours + " (" + hours.GetType().Name + ")", () =>
            {
                var row = ValidRequest();
                row[Schema.Hours] = hours;
                Reject(() => Rules.Content(row), "Hours must");
            });
        }
        foreach (var pair in new[] { (0, 1), (0, 1440), (1439, 1440) })
        {
            var interval = pair;
            Test("Content accepts interval " + interval, () =>
            {
                var row = ValidRequest();
                row[Schema.StartMinute] = interval.Item1;
                row[Schema.EndMinute] = interval.Item2;
                Rules.Content(row);
            });
        }
        foreach (var pair in new[] { (-1, 60), (1440, 1440), (0, 0), (0, 1441), (60, 60), (61, 60) })
        {
            var interval = pair;
            Test("Content rejects interval " + interval, () =>
            {
                var row = ValidRequest();
                row[Schema.StartMinute] = interval.Item1;
                row[Schema.EndMinute] = interval.Item2;
                Reject(() => Rules.Content(row), "Minutes must");
            });
        }
        Test("Content rejects decimal minute", () =>
        {
            var row = ValidRequest();
            row[Schema.StartMinute] = 540m;
            Reject(() => Rules.Content(row), "leap_startminute");
        });
        foreach (var year in new[] { 1900, 2100 })
        {
            var boundary = year;
            Test("Content accepts date year " + boundary, () =>
            {
                var row = ValidRequest();
                row[Schema.LeaveDate] = new DateTime(boundary, 12, 31, 23, 59, 59, DateTimeKind.Utc);
                Rules.Content(row);
                var date = (DateTime)row[Schema.LeaveDate];
                Check(date == new DateTime(boundary, 12, 31) && date.Kind == DateTimeKind.Unspecified,
                    "DateOnly must preserve wall-calendar date without timezone conversion.");
            });
        }
        foreach (var year in new[] { 1899, 2101 })
        {
            var boundary = year;
            Test("Content rejects date year " + boundary, () =>
            {
                var row = ValidRequest();
                row[Schema.LeaveDate] = new DateTime(boundary, 1, 1);
                Reject(() => Rules.Content(row), "1900 and 2100");
            });
        }
        for (var index = 0; index < 13; index++)
        {
            var category = 100000000 + index;
            Test("Content accepts category " + category, () =>
            {
                var row = ValidRequest();
                row[Schema.Category] = new OptionSetValue(category);
                Rules.Content(row);
            });
        }
        foreach (var value in new object[] { new OptionSetValue(99999999), new OptionSetValue(100000013), 100000000 })
        {
            var category = value;
            Test("Content rejects category " + category, () =>
            {
                var row = ValidRequest();
                row[Schema.Category] = category;
                Reject(() => Rules.Content(row), category is OptionSetValue ? "13 LEAP categories" : "leap_category");
            });
        }
        Test("Content allows overlapping requests and explicit hours independent of duration", () =>
        {
            var first = ValidRequest();
            var second = ValidRequest();
            second.Id = Guid.NewGuid();
            second[Schema.StartMinute] = 570;
            first[Schema.Hours] = 0;
            second[Schema.Hours] = 24;
            Rules.Content(first);
            Rules.Content(second);
        });
    }

    private static void IdentityTests()
    {
        foreach (var mode in new[] { 0, 1, 2 })
        {
            var accessMode = mode;
            Test("Identity accepts interactive access mode " + accessMode, () =>
            {
                var service = Users();
                service.OnRetrieve = (name, id, columns) =>
                {
                    Check(columns.Columns.Contains("applicationid") && columns.Columns.Contains("domainname"));
                    var user = User(id);
                    user["accessmode"] = new OptionSetValue(accessMode);
                    user["applicationid"] = Guid.Empty;
                    return user;
                };
                Rules.EnabledInternalUser(service, First);
            });
        }
        var changes = new Dictionary<string, object>
        {
            ["isdisabled"] = true,
            ["applicationid"] = Guid.NewGuid(),
            ["azureactivedirectoryobjectid"] = Guid.Empty,
            ["accessmode"] = new OptionSetValue(4),
            ["domainname"] = "guest#eXt#@example.invalid"
        };
        foreach (var change in changes)
        {
            var field = change;
            Test("Identity rejects " + field.Key, () =>
            {
                var service = Users();
                service.OnRetrieve = (name, id, columns) =>
                {
                    var row = User(id);
                    row[field.Key] = field.Value;
                    return row;
                };
                Reject(() => Rules.EnabledInternalUser(service, First), "enabled, interactive internal");
            });
        }
        foreach (var field in new[] { "isdisabled", "accessmode", "domainname", "azureactivedirectoryobjectid" })
        {
            var absent = field;
            Test("Identity rejects missing " + absent, () =>
            {
                var service = Users();
                service.OnRetrieve = (name, id, columns) =>
                {
                    var row = User(id);
                    row.Attributes.Remove(absent);
                    return row;
                };
                Reject(() => Rules.EnabledInternalUser(service, First),
                    absent == "isdisabled" || absent == "accessmode" ? absent : "enabled, interactive internal");
            });
        }
        Test("Identity rejects whitespace domain", () =>
        {
            var service = Users();
            service.OnRetrieve = (name, id, columns) =>
            {
                var row = User(id);
                row["domainname"] = " \t";
                return row;
            };
            Reject(() => Rules.EnabledInternalUser(service, First), "enabled, interactive internal");
        });
    }

    private static void ApproverTests()
    {
        Test("Approvers accepts one and skips absent optionals", () =>
            Check(Rules.Approvers(ValidRequest(), Requester, Users()).Single().Id == First));
        Test("Approvers accepts three distinct enabled internal users", () =>
        {
            var row = ValidRequest();
            row[Schema.Approver2] = new EntityReference("systemuser", Second);
            row[Schema.Approver3] = new EntityReference("systemuser", Third);
            Check(Rules.Approvers(row, Requester, Users()).Select(item => item.Id)
                .SequenceEqual(new[] { First, Second, Third }));
        });
        Test("Approvers accepts blank second with third selected", () =>
        {
            var row = ValidRequest();
            row[Schema.Approver2] = null;
            row[Schema.Approver3] = new EntityReference("systemuser", Third);
            Check(Rules.Approvers(row, Requester, Users()).Count == 2);
        });
        foreach (var value in new object[] { null, new EntityReference("team", First), new EntityReference("systemuser", Guid.Empty), "name" })
        {
            var reference = value;
            Test("Approvers rejects invalid first " + (reference ?? "<blank>"), () =>
            {
                var row = ValidRequest();
                row[Schema.Approver1] = reference;
                Reject(() => Rules.Approvers(row, Requester, Users()), "leap_approver1id");
            });
        }
        foreach (var field in Schema.Approvers)
        {
            var self = field;
            Test("Approvers rejects self in " + self, () =>
            {
                var row = ValidRequest();
                row[self] = new EntityReference("systemuser", Requester);
                Reject(() => Rules.Approvers(row, Requester, Users()), "Self-approval");
            });
        }
        Test("Approvers rejects duplicate first and second", () =>
        {
            var row = ValidRequest();
            row[Schema.Approver2] = new EntityReference("systemuser", First);
            Reject(() => Rules.Approvers(row, Requester, Users()), "distinct");
        });
        Test("Approvers rejects duplicate second and third", () =>
        {
            var row = ValidRequest();
            row[Schema.Approver2] = new EntityReference("systemuser", Second);
            row[Schema.Approver3] = new EntityReference("systemuser", Second);
            Reject(() => Rules.Approvers(row, Requester, Users()), "distinct");
        });
        Test("Approvers validates optional user's eligibility", () =>
        {
            var row = ValidRequest();
            row[Schema.Approver3] = new EntityReference("systemuser", Third);
            var service = Users();
            service.OnRetrieve = (name, id, columns) =>
            {
                var user = User(id);
                user["isdisabled"] = id == Third;
                return user;
            };
            Reject(() => Rules.Approvers(row, Requester, service), "enabled, interactive internal");
        });
    }

    private static PluginHost UpdateHost(Entity target = null)
    {
        var host = new PluginHost("Update", 20, Requester, Users());
        host.Inputs["Target"] = target ?? new Entity(Schema.Table, RowId) { [Schema.ApprovalId] = "offline-approval" };
        host.Images["Before"] = Before();
        host.Service.OnRetrieveMultiple = query => Rows(new Entity("role", Guid.NewGuid()));
        return host;
    }

    private static void GuardTests()
    {
        Test("Guard Create derives requester owner status year and trims date", () =>
        {
            var row = ValidRequest();
            row[Schema.Requester] = new EntityReference("systemuser", Third);
            row[Schema.Owner] = new EntityReference("systemuser", Third);
            var host = new PluginHost("Create", 20, Requester, Users());
            host.Inputs["Target"] = row;
            new RequestGuard().Execute(host);
            Check(((EntityReference)row[Schema.Requester]).Id == Requester
                && ((EntityReference)row[Schema.Owner]).Id == Requester);
            Check(((OptionSetValue)row[Schema.Status]).Value == Schema.Pending && (int)row[Schema.Year] == 2026);
            Check(Schema.ServerFields.Where(field => field != Schema.Year).All(field => row[field] == null));
        });
        foreach (var field in Schema.ServerFields)
        {
            var protectedField = field;
            Test("Guard Create rejects supplied server field " + protectedField, () =>
            {
                var row = ValidRequest();
                row[protectedField] = "forged";
                var host = new PluginHost("Create", 20, Requester, Users());
                host.Inputs["Target"] = row;
                Reject(() => new RequestGuard().Execute(host), "managed by IRS LEAP");
            });
        }
        Test("Guard Create rejects non-Pending initial status", () =>
        {
            var row = ValidRequest();
            row[Schema.Status] = new OptionSetValue(Schema.Approved);
            var host = new PluginHost("Create", 20, Requester, Users());
            host.Inputs["Target"] = row;
            Reject(() => new RequestGuard().Execute(host), "New requests must be Pending");
        });
        Test("Guard Create allows overlapping saves without overlap query", () =>
        {
            var host = new PluginHost("Create", 20, Requester, Users());
            host.Inputs["Target"] = ValidRequest();
            new RequestGuard().Execute(host);
            host.Inputs["Target"] = ValidRequest();
            new RequestGuard().Execute(host);
            Check(host.Service.Queries.Count == 0);
        });
        foreach (var field in new[] { Schema.Name, Schema.Owner, Schema.Requester, Schema.Year, Schema.Hours, Schema.Approver1, Schema.DecidedOn })
        {
            var immutable = field;
            Test("Guard Update rejects protected field presence " + immutable, () =>
            {
                var target = new Entity(Schema.Table, RowId);
                target[immutable] = Before().Contains(immutable) ? Before()[immutable] : null;
                var host = UpdateHost(target);
                Reject(() => new RequestGuard().Execute(host), "immutable");
            });
        }
        Test("Guard rejects Assign", () =>
        {
            var host = new PluginHost("Assign", 20, Requester, Users());
            host.Inputs["Target"] = new EntityReference(Schema.Table, RowId);
            Reject(() => new RequestGuard().Execute(host), "cannot be reassigned");
        });
        Test("Guard rejects untrusted workflow caller", () =>
        {
            var host = UpdateHost();
            host.Service.OnRetrieveMultiple = query => Rows();
            Reject(() => new RequestGuard().Execute(host), "Only IRS LEAP Automation");
            Check(host.Service.Queries.Count == 2, "Both direct-role and team-role checks must run.");
        });
        Test("Guard accepts trusted team role and scopes membership query", () =>
        {
            var host = UpdateHost();
            host.Service.OnRetrieveMultiple = query =>
            {
                var role = (QueryExpression)query;
                Check(role.EntityName == "role" && role.TopCount == 1);
                Check(role.Criteria.Conditions.Single().Values.Cast<string>()
                    .SequenceEqual(new[] { "IRS LEAP Automation", "System Administrator" }));
                var link = role.LinkEntities.Single();
                if (link.LinkToEntityName == "systemuserroles")
                {
                    Check((Guid)link.LinkCriteria.Conditions.Single().Values.Single() == Requester);
                    return Rows();
                }
                Check(link.LinkToEntityName == "teamroles");
                var member = link.LinkEntities.Single();
                Check(member.LinkToEntityName == "teammembership"
                    && (Guid)member.LinkCriteria.Conditions.Single().Values.Single() == Requester);
                return Rows(new Entity("role", Guid.NewGuid()));
            };
            new RequestGuard().Execute(host);
        });
        Test("Guard requires Before pre-image", () =>
        {
            var host = UpdateHost();
            host.Images.Clear();
            Reject(() => new RequestGuard().Execute(host), "pre-image named Before");
        });
        Test("Guard rejects incomplete Before pre-image", () =>
        {
            var host = UpdateHost();
            host.Images["Before"].Attributes.Remove(Schema.Year);
            Reject(() => new RequestGuard().Execute(host), "missing required column leap_year");
        });
        foreach (var status in new[] { Schema.Approved, Schema.Denied, Schema.Expired })
        {
            var final = status;
            Test("Guard protects finalized request status " + final, () =>
            {
                var host = UpdateHost();
                host.Images["Before"][Schema.Status] = new OptionSetValue(final);
                Reject(() => new RequestGuard().Execute(host), "Finalized LEAP requests");
            });
        }
        foreach (var status in new[] { Schema.Approved, Schema.Denied })
        {
            var decision = status;
            Test("Guard finalizes selected approver decision " + decision, () =>
            {
                var target = new Entity(Schema.Table, RowId)
                {
                    [Schema.Status] = new OptionSetValue(decision),
                    [Schema.DecidedBy] = new EntityReference("systemuser", First),
                    [Schema.DecisionComments] = "Offline decision"
                };
                var host = UpdateHost(target);
                var start = DateTime.UtcNow;
                new RequestGuard().Execute(host);
                var decided = (DateTime)target[Schema.DecidedOn];
                Check(decided >= start && decided <= DateTime.UtcNow && decided.Kind == DateTimeKind.Utc);
            });
        }
        Test("Guard rejects unselected decider", () =>
        {
            var target = new Entity(Schema.Table, RowId)
            {
                [Schema.Status] = new OptionSetValue(Schema.Approved),
                [Schema.DecidedBy] = new EntityReference("systemuser", Third)
            };
            Reject(() => new RequestGuard().Execute(UpdateHost(target)), "selected approvers");
        });
        Test("Guard rejects decision comments without final decision", () =>
        {
            var target = new Entity(Schema.Table, RowId) { [Schema.DecisionComments] = "not final" };
            Reject(() => new RequestGuard().Execute(UpdateHost(target)), "Decision attribution and comments require");
        });
        Test("Guard disallows approval ID alongside final decision", () =>
        {
            var target = new Entity(Schema.Table, RowId)
            {
                [Schema.Status] = new OptionSetValue(Schema.Approved),
                [Schema.ApprovalId] = "id",
                [Schema.DecidedBy] = new EntityReference("systemuser", First)
            };
            Reject(() => new RequestGuard().Execute(UpdateHost(target)), "while the request remains Pending");
        });
        Test("Guard expires without decision attribution", () =>
        {
            var target = new Entity(Schema.Table, RowId) { [Schema.Status] = new OptionSetValue(Schema.Expired) };
            new RequestGuard().Execute(UpdateHost(target));
            Check(target[Schema.DecidedBy] == null && target[Schema.DecidedOn] == null && target[Schema.DecisionComments] == null);
        });
        Test("Guard fails closed on wrong stage", () =>
        {
            var host = new PluginHost("Create", 40, Requester, Users());
            host.Inputs["Target"] = ValidRequest();
            Reject(() => new RequestGuard().Execute(host), "registration is incorrect");
        });
        Test("Guard fails closed on asynchronous execution", () =>
        {
            var host = new PluginHost("Create", 20, Requester, Users());
            host.Values["Mode"] = 1;
            host.Inputs["Target"] = ValidRequest();
            Reject(() => new RequestGuard().Execute(host), "registration is incorrect");
        });
    }

    private static void ShareTests()
    {
        Test("Share grants selected approvers read only inside Create transaction", () =>
        {
            var row = Before();
            row[Schema.Approver2] = new EntityReference("systemuser", Second);
            var service = Users();
            service.OnRetrieve = (name, id, columns) => name == Schema.Table ? row : User(id);
            service.OnExecute = request =>
            {
                var grant = (GrantAccessRequest)request;
                Check(grant.Target.LogicalName == Schema.Table && grant.Target.Id == RowId);
                Check(grant.PrincipalAccess.AccessMask == AccessRights.ReadAccess);
                return new GrantAccessResponse();
            };
            var host = new PluginHost("Create", 40, Requester, service);
            host.Inputs["Target"] = ValidRequest();
            new RequestShare().Execute(host);
            Check(service.Requests.Cast<GrantAccessRequest>().Select(item => item.PrincipalAccess.Principal.Id)
                .SequenceEqual(new[] { First, Second }));
        });
        Test("Share rejects nontransactional step", () =>
        {
            var host = new PluginHost("Create", 40, Requester, Users());
            host.Values["IsInTransaction"] = false;
            host.Inputs["Target"] = ValidRequest();
            Reject(() => new RequestShare().Execute(host), "inside the Create transaction");
            Check(host.Service.Retrievals == 0 && host.Service.Requests.Count == 0);
        });
        Test("Share rejects owner not derived by guard", () =>
        {
            var service = Users();
            service.OnRetrieve = (name, id, columns) =>
            {
                var row = Before();
                row[Schema.Owner] = new EntityReference("systemuser", Third);
                return row;
            };
            var host = new PluginHost("Create", 40, Requester, service);
            host.Inputs["Target"] = ValidRequest();
            Reject(() => new RequestShare().Execute(host), "derive the requester and owner");
        });
    }

    private static EntityCollection Rows(params Entity[] rows)
    {
        var result = new EntityCollection();
        foreach (var row in rows)
            result.Entities.Add(row);
        return result;
    }

    private static Entity Aggregate(object approved, object recorded, object hours)
    {
        return new Entity(Schema.Table)
        {
            ["approvedrequests"] = new AliasedValue(Schema.Table, Schema.Id, approved),
            ["recordedhours"] = new AliasedValue(Schema.Table, Schema.Hours, recorded),
            ["totalhours"] = new AliasedValue(Schema.Table, Schema.Hours, hours)
        };
    }

    private static PluginHost AnnualHost(Guid caller, Entity totals = null)
    {
        var host = new PluginHost(Schema.AnnualApi, 30, caller, Users());
        host.Inputs["RequesterId"] = Requester.ToString("D");
        host.Inputs["CalendarYear"] = 2026;
        host.Service.OnRetrieveMultiple = query =>
            query is FetchExpression ? Rows(totals ?? Aggregate(3, 2, 8)) : Rows(new Entity(Schema.Table, RowId));
        return host;
    }

    private static void AnnualTests()
    {
        Test("Annual requester sees approved sum and blank-versus-zero count", () =>
        {
            var host = AnnualHost(Requester, Aggregate(3, 2, 8));
            new AnnualTotal().Execute(host);
            Check((int)host.Outputs["TotalHours"] == 8 && (int)host.Outputs["MissingHours"] == 1
                && (int)host.Outputs["ApprovedRequests"] == 3
                && (string)host.Outputs["RequesterName"] == "Offline Employee");
            Check(host.Service.Queries.Count == 1 && host.Service.Queries[0] is FetchExpression,
                "The requester must not need an approver authorization query.");
            var fetch = XDocument.Parse(((FetchExpression)host.Service.Queries[0]).Query);
            var conditions = fetch.Descendants("condition").ToDictionary(item => (string)item.Attribute("attribute"),
                item => (string)item.Attribute("value"));
            Check(conditions[Schema.Requester] == Requester.ToString("D") && conditions[Schema.Year] == "2026"
                && conditions[Schema.Status] == Schema.Approved.ToString());
            Check(fetch.Descendants("attribute").Any(item => (string)item.Attribute("alias") == "recordedhours"
                && (string)item.Attribute("aggregate") == "countcolumn"));
        });
        Test("Annual selected-approver authorization is lifetime across any saved year/status", () =>
        {
            var host = AnnualHost(First);
            host.Inputs["CalendarYear"] = 1900;
            host.Service.OnRetrieveMultiple = query =>
            {
                if (query is FetchExpression fetch)
                {
                    Check(fetch.Query.Contains("value='1900'"));
                    return Rows(Aggregate(0, 0, null));
                }
                var authorization = (QueryExpression)query;
                Check(authorization.EntityName == Schema.Table && authorization.TopCount == 1);
                Check(authorization.Criteria.Conditions.Count == 1
                    && authorization.Criteria.Conditions[0].AttributeName == Schema.Requester
                    && (Guid)authorization.Criteria.Conditions[0].Values.Single() == Requester,
                    "Approver authorization must not be restricted to the requested year or status.");
                var selected = authorization.Criteria.Filters.Single();
                Check(selected.FilterOperator == LogicalOperator.Or
                    && selected.Conditions.Select(item => item.AttributeName).SequenceEqual(Schema.Approvers)
                    && selected.Conditions.All(item => (Guid)item.Values.Single() == First));
                return Rows(new Entity(Schema.Table, RowId));
            };
            new AnnualTotal().Execute(host);
            Check((int)host.Outputs["TotalHours"] == 0);
        });
        Test("Annual denies unrelated caller before reading name or aggregate", () =>
        {
            var host = AnnualHost(Third);
            host.Service.OnRetrieveMultiple = query => Rows();
            Reject(() => new AnnualTotal().Execute(host), "only to the requester or an assigned approver");
            Check(host.Service.Retrievals == 0 && host.Service.Queries.Count == 1 && host.Outputs.Count == 0);
        });
        foreach (var value in new object[] { null, "", "not-a-guid", Guid.Empty.ToString(), Requester })
        {
            var requester = value;
            Test("Annual rejects requester input " + (requester ?? "<null>"), () =>
            {
                var host = AnnualHost(Requester);
                host.Inputs["RequesterId"] = requester;
                Reject(() => new AnnualTotal().Execute(host), "RequesterId");
                Check(host.Service.Retrievals == 0 && host.Service.Queries.Count == 0);
            });
        }
        foreach (var value in new object[] { 1899, 2101, 2026m, "2026", null })
        {
            var year = value;
            Test("Annual rejects year input " + (year ?? "<null>"), () =>
            {
                var host = AnnualHost(Requester);
                host.Inputs["CalendarYear"] = year;
                Reject(() => new AnnualTotal().Execute(host), "CalendarYear");
            });
        }
        Test("Annual accepts upper year boundary", () =>
        {
            var host = AnnualHost(Requester);
            host.Inputs["CalendarYear"] = 2100;
            new AnnualTotal().Execute(host);
        });
        Test("Annual empty aggregate yields zero outputs", () =>
        {
            var host = AnnualHost(Requester, Aggregate(0, 0, null));
            new AnnualTotal().Execute(host);
            Check((int)host.Outputs["TotalHours"] == 0 && (int)host.Outputs["MissingHours"] == 0
                && (int)host.Outputs["ApprovedRequests"] == 0);
        });
        Test("Annual explicit zero is recorded, not missing", () =>
        {
            var host = AnnualHost(Requester, Aggregate(1, 1, 0));
            new AnnualTotal().Execute(host);
            Check((int)host.Outputs["TotalHours"] == 0 && (int)host.Outputs["MissingHours"] == 0);
        });
        Test("Annual missing recorded hours counts every approved row", () =>
        {
            var host = AnnualHost(Requester, Aggregate(2, 0, null));
            new AnnualTotal().Execute(host);
            Check((int)host.Outputs["TotalHours"] == 0 && (int)host.Outputs["MissingHours"] == 2);
        });
        foreach (var value in new object[] { -1, 1.5m, (long)int.MaxValue + 1 })
        {
            var aggregate = value;
            Test("Annual rejects invalid aggregate " + aggregate, () =>
            {
                var host = AnnualHost(Requester, Aggregate(2, 1, aggregate));
                Reject(() => new AnnualTotal().Execute(host), "outside the supported integer range");
            });
        }
        Test("Annual rejects recorded count above approved count", () =>
        {
            var host = AnnualHost(Requester, Aggregate(1, 2, 2));
            Reject(() => new AnnualTotal().Execute(host), "inconsistent annual aggregate");
        });
        Test("Annual refuses incomplete aggregate result", () =>
        {
            var host = AnnualHost(Requester);
            host.Service.OnRetrieveMultiple = query => Rows();
            Reject(() => new AnnualTotal().Execute(host), "complete annual aggregate");
        });
        Test("Annual reports service aggregate fault without fallback or partial outputs", () =>
        {
            var host = AnnualHost(Requester);
            host.Service.OnRetrieveMultiple = query => throw new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { Message = "Offline aggregate limit simulation" });
            Reject(() => new AnnualTotal().Execute(host), "No partial total was returned");
            Check(host.Service.Queries.Count == 1 && host.Outputs.Count == 0);
        });
    }
}

internal sealed class StubService : IOrganizationService
{
    internal Func<string, Guid, ColumnSet, Entity> OnRetrieve;
    internal Func<QueryBase, EntityCollection> OnRetrieveMultiple;
    internal Func<OrganizationRequest, OrganizationResponse> OnExecute;
    internal readonly List<QueryBase> Queries = new List<QueryBase>();
    internal readonly List<OrganizationRequest> Requests = new List<OrganizationRequest>();
    internal int Retrievals;

    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
    {
        Retrievals++;
        return OnRetrieve == null ? throw new InvalidOperationException("Unexpected Retrieve.") : OnRetrieve(entityName, id, columnSet);
    }

    public EntityCollection RetrieveMultiple(QueryBase query)
    {
        Queries.Add(query);
        return OnRetrieveMultiple == null ? throw new InvalidOperationException("Unexpected RetrieveMultiple.") : OnRetrieveMultiple(query);
    }

    public OrganizationResponse Execute(OrganizationRequest request)
    {
        Requests.Add(request);
        return OnExecute == null ? throw new InvalidOperationException("Unexpected Execute.") : OnExecute(request);
    }

    public Guid Create(Entity entity) => throw new InvalidOperationException("Unexpected Create.");
    public void Update(Entity entity) => throw new InvalidOperationException("Unexpected Update.");
    public void Delete(string entityName, Guid id) => throw new InvalidOperationException("Unexpected Delete.");
    public void Associate(string entityName, Guid id, Relationship relationship, EntityReferenceCollection relatedEntities)
        => throw new InvalidOperationException("Unexpected Associate.");
    public void Disassociate(string entityName, Guid id, Relationship relationship, EntityReferenceCollection relatedEntities)
        => throw new InvalidOperationException("Unexpected Disassociate.");
}

internal sealed class PluginHost : IServiceProvider, IOrganizationServiceFactory, ITracingService
{
    internal readonly ParameterCollection Inputs = new ParameterCollection();
    internal readonly ParameterCollection Outputs = new ParameterCollection();
    internal readonly EntityImageCollection Images = new EntityImageCollection();
    internal readonly Dictionary<string, object> Values;
    internal readonly StubService Service;
    private readonly IPluginExecutionContext context;

    internal PluginHost(string message, int stage, Guid caller, StubService service)
    {
        Service = service;
        context = DispatchProxy.Create<IPluginExecutionContext, PropertyContext>();
        Values = ((PropertyContext)context).Values;
        Values["MessageName"] = message;
        Values["Stage"] = stage;
        Values["Mode"] = 0;
        Values["InitiatingUserId"] = caller;
        Values["CorrelationId"] = Guid.NewGuid();
        Values["PrimaryEntityId"] = Guid.Parse("55555555-5555-5555-5555-555555555555");
        Values["IsInTransaction"] = true;
        Values["InputParameters"] = Inputs;
        Values["OutputParameters"] = Outputs;
        Values["PreEntityImages"] = Images;
    }

    public object GetService(Type type)
    {
        if (type == typeof(IPluginExecutionContext))
            return context;
        if (type == typeof(IOrganizationServiceFactory) || type == typeof(ITracingService))
            return this;
        throw new InvalidOperationException("Unexpected service " + type);
    }

    public IOrganizationService CreateOrganizationService(Guid? userId)
    {
        if (userId != null)
            throw new InvalidOperationException("Expected explicit system organization service.");
        return Service;
    }

    public void Trace(string format, params object[] args) { }
}

public class PropertyContext : DispatchProxy
{
    internal readonly Dictionary<string, object> Values = new Dictionary<string, object>();

    protected override object Invoke(MethodInfo targetMethod, object[] args)
    {
        object value;
        if (targetMethod.Name.StartsWith("get_", StringComparison.Ordinal)
            && Values.TryGetValue(targetMethod.Name.Substring(4), out value))
            return value;
        throw new InvalidOperationException("Unexpected context member " + targetMethod.Name);
    }
}
