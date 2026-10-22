using System;
using System.Collections.Generic;

namespace IrsLeap.Plugins
{
    internal static class Schema
    {
        internal const string Table = "leap_request";
        internal const string Id = "leap_requestid";
        internal const string Name = "leap_name";
        internal const string Description = "leap_description";
        internal const string LeaveDate = "leap_leavedate";
        internal const string StartMinute = "leap_startminute";
        internal const string EndMinute = "leap_endminute";
        internal const string Hours = "leap_hours";
        internal const string Category = "leap_category";
        internal const string Status = "leap_status";
        internal const string Year = "leap_year";
        internal const string Requester = "leap_requesterid";
        internal const string Approver1 = "leap_approver1id";
        internal const string Approver2 = "leap_approver2id";
        internal const string Approver3 = "leap_approver3id";
        internal const string DecidedBy = "leap_decidedbyid";
        internal const string DecidedOn = "leap_decidedon";
        internal const string DecisionComments = "leap_decisioncomments";
        internal const string ApprovalId = "leap_approvalid";
        internal const string ProcessingNote = "leap_processingnote";
        internal const string Owner = "ownerid";
        internal const string AnnualApi = "leap_GetAnnualTotal";
        internal const int Pending = 100000000;
        internal const int Approved = 100000001;
        internal const int Denied = 100000002;
        internal const int Expired = 100000003;
        internal static readonly string[] Approvers = { Approver1, Approver2, Approver3 };
        internal static readonly string[] ServerFields =
        {
            Year, DecidedBy, DecidedOn, DecisionComments, ApprovalId, ProcessingNote
        };
        internal static readonly HashSet<string> CreateFields = new HashSet<string>(StringComparer.Ordinal)
        {
            Id, Name, Description, LeaveDate, StartMinute, EndMinute, Hours, Category,
            Status, Requester, Owner, Approver1, Approver2, Approver3,
            Year, DecidedBy, DecidedOn, DecisionComments, ApprovalId, ProcessingNote,
            "owningbusinessunit", "owninguser", "owningteam",
            "createdby", "createdon", "createdonbehalfby",
            "modifiedby", "modifiedon", "modifiedonbehalfby",
            "statecode", "statuscode", "versionnumber", "timezoneruleversionnumber",
            "utcconversiontimezonecode"
        };
        internal static readonly HashSet<string> UpdateFields = new HashSet<string>(StringComparer.Ordinal)
        {
            Id, Status, DecidedBy, DecisionComments, ApprovalId, ProcessingNote,
            "modifiedby", "modifiedon", "modifiedonbehalfby", "versionnumber"
        };
        internal static readonly string[] BeforeColumns =
        {
            Name, Description, LeaveDate, StartMinute, EndMinute, Hours, Category, Status,
            Year, Requester, Owner, Approver1, Approver2, Approver3,
            DecidedBy, DecidedOn, DecisionComments, ApprovalId, ProcessingNote
        };
    }
}
