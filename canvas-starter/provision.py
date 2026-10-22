"""Administrator-run, solution-first provisioning for the IRS LEAP workshop."""

import argparse
import base64
import json
import os
from pathlib import Path
import sys
import time

import requests

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "scripts" if (ROOT / "scripts").exists()
                       else ROOT.parent / "scripts"))
from auth import get_credential, get_token, load_env
from PowerPlatform.Dataverse.client import DataverseClient
from PowerPlatform.Dataverse.models.relationship import (
    LookupAttributeMetadata, OneToManyRelationshipMetadata)
from PowerPlatform.Dataverse.models.labels import Label, LocalizedLabel

load_env()
URL = os.environ["DATAVERSE_URL"].rstrip("/")
SOLUTION = "IRSLEAPCanvasStarter"
PREFIX = "leap"
TABLE = "leap_request"
CATEGORIES = [
    "Annual", "Sick", "Admin",
    "Exception A: Management-Requested Work During Approved Leave",
    "Exception B: Office Time Over 50% of Duty Hours",
    "Exception C: Leave Exceeding 50% of Duty Hours",
    "Exception D: Medical Reason (Employee or Dependent)",
    "Exception E: Emergency Home Repairs",
    "Exception F: Emergency Caregiving Needs",
    "Exception G: Other Exigent Circumstances",
    "Exception H: Interim 90-Day RA Exception",
    "No Exception I: Authorized Situation Telework Instances", "Other",
]


def label(text):
    return {"LocalizedLabels": [{"Label": text, "LanguageCode": 1033}]}


class Provisioner:
    def __init__(self):
        self.client = DataverseClient(URL, get_credential())
        self.session = requests.Session()
        self.session.headers.update({
            "Authorization": "Bearer " + get_token(),
            "Content-Type": "application/json",
            "OData-MaxVersion": "4.0",
            "OData-Version": "4.0",
            "MSCRM.SolutionUniqueName": SOLUTION,
        })
        identity = self.api("GET", "WhoAmI")
        principal = self.api(
            "GET", f"systemusers({identity['UserId']})?$select=fullname"
            "&$expand=systemuserroles_association($select=name)")
        roles = principal.get("systemuserroles_association", [])
        if not any(role["name"] == "System Administrator" for role in roles):
            raise RuntimeError(
                "Provisioning requires an authorized account with the direct "
                "Dataverse System Administrator role. No role will be assigned by this script.")
        print("Provisioning target:", URL, flush=True)
        print("Provisioning user:", principal["fullname"], flush=True)

    def api(self, method, path, body=None):
        # Metadata with explicit ownership/date behavior, role privileges and
        # platform actions are not exposed by the Python SDK.
        response = self.session.request(
            method, f"{URL}/api/data/v9.2/{path}", json=body, timeout=120)
        if not response.ok:
            raise RuntimeError(
                f"{method} {path}: {response.status_code} {response.text}")
        return response.json() if response.content else None

    def records(self, table, **kwargs):
        return [row for page in self.client.records.get(table, **kwargs) for row in page]

    def ensure_record(self, table, filter_text, data, id_column):
        rows = self.records(table, filter=filter_text, select=[id_column])
        if rows:
            return rows[0][id_column]
        return self.client.records.create(table, data)

    def add_component(self, component_id, component_type):
        self.api("POST", "AddSolutionComponent", {
            "ComponentId": component_id,
            "ComponentType": component_type,
            "SolutionUniqueName": SOLUTION,
            "AddRequiredComponents": False,
        })

    def create_solution(self):
        publisher_id = self.ensure_record(
            "publisher", "uniquename eq 'IRSLEAP'", {
                "uniquename": "IRSLEAP", "friendlyname": "IRS LEAP",
                "customizationprefix": PREFIX,
                "customizationoptionvalueprefix": 10000,
                "description": "IRS LEAP workshop publisher; not an IRS production certification.",
            }, "publisherid")
        solution_id = self.ensure_record(
            "solution", f"uniquename eq '{SOLUTION}'", {
                "uniquename": SOLUTION,
                "friendlyname": "IRS LEAP Canvas Starter",
                "version": "1.0.0.0",
                "description": "Dataverse foundation for the one-day IRS LEAP Canvas workshop.",
                "publisherid@odata.bind": f"/publishers({publisher_id})",
            }, "solutionid")
        print("Solution:", solution_id, flush=True)
        return solution_id

    @staticmethod
    def attribute(schema, title, kind, required=False, **kwargs):
        return {
            "@odata.type": f"Microsoft.Dynamics.CRM.{kind}AttributeMetadata",
            "SchemaName": schema,
            "DisplayName": label(title),
            "RequiredLevel": {"Value": "ApplicationRequired" if required else "None"},
            **kwargs,
        }

    def create_schema(self):
        existing = self.api(
            "GET", f"EntityDefinitions?$select=MetadataId,LogicalName&$filter=LogicalName eq '{TABLE}'")
        if not existing["value"]:
            attr = self.attribute
            attributes = [
                attr("leap_Name", "Title", "String", True,
                     MaxLength=120, IsPrimaryName=True),
                attr("leap_Description", "Description", "Memo", MaxLength=1000),
                attr("leap_LeaveDate", "Leave Date", "DateTime", True,
                     Format="DateOnly", DateTimeBehavior={"Value": "DateOnly"}),
                attr("leap_StartMinute", "Start Minute", "Integer", True,
                     MinValue=0, MaxValue=1439, Format="None"),
                attr("leap_EndMinute", "End Minute", "Integer", True,
                     MinValue=1, MaxValue=2147483647, Format="None"),
                attr("leap_Hours", "Leave Hours", "Integer",
                     MinValue=0, MaxValue=2147483647, Format="None"),
                attr("leap_Year", "Calendar Year", "Integer",
                     MinValue=1900, MaxValue=2100, Format="None"),
                attr("leap_Category", "Leave Category", "Picklist", True,
                     OptionSet={
                         "IsGlobal": False, "OptionSetType": "Picklist",
                         "Options": [{"Value": 100000000 + i, "Label": label(name)}
                                     for i, name in enumerate(CATEGORIES)],
                     }),
                attr("leap_Status", "Request Status", "Picklist",
                     DefaultFormValue=100000000,
                     OptionSet={
                         "IsGlobal": False, "OptionSetType": "Picklist",
                         "Options": [{"Value": 100000000 + i, "Label": label(name)}
                                     for i, name in enumerate(
                                         ["Pending", "Approved", "Denied", "Expired"])],
                     }),
                attr("leap_DecidedOn", "Decided On", "DateTime",
                     Format="DateAndTime", DateTimeBehavior={"Value": "UserLocal"}),
                attr("leap_DecisionComments", "Decision Comments", "Memo", MaxLength=1000),
                attr("leap_ApprovalId", "Approval ID", "String", MaxLength=100),
                attr("leap_ProcessingNote", "Processing Note", "String", MaxLength=500),
            ]
            self.api("POST", "EntityDefinitions", {
                "@odata.type": "Microsoft.Dynamics.CRM.EntityMetadata",
                "SchemaName": "leap_Request",
                "DisplayName": label("LEAP Request"),
                "DisplayCollectionName": label("LEAP Requests"),
                "Description": label("One leave category per request with start/end date-time and 15-minute units."),
                "OwnershipType": "UserOwned",
                "PrimaryNameAttribute": "leap_name",
                "IsActivity": False, "HasActivities": False, "HasNotes": False,
                "IsAuditEnabled": {"Value": True},
                "IsOptimisticConcurrencyEnabled": True,
                "Attributes": attributes,
            })
            time.sleep(5)
        info = self.api("GET", f"EntityDefinitions(LogicalName='{TABLE}')?$select=MetadataId,EntitySetName")
        self.add_component(info["MetadataId"], 1)
        columns = self.api(
            "GET", f"EntityDefinitions(LogicalName='{TABLE}')/Attributes?$select=LogicalName")
        names = {column["LogicalName"] for column in columns["value"]}
        for schema, title in [
                ("leap_RequesterId", "Requester"),
                ("leap_Approver1Id", "Approver 1"),
                ("leap_Approver2Id", "Approver 2"),
                ("leap_Approver3Id", "Approver 3"),
                ("leap_DecidedById", "Decided By")]:
            if schema.lower() not in names:
                self.client.tables.create_one_to_many_relationship(
                    LookupAttributeMetadata(
                        schema_name=schema,
                        display_name=Label([LocalizedLabel(title, 1033)]),
                        required_level="ApplicationRequired" if title == "Approver 1" else "None"),
                    OneToManyRelationshipMetadata(
                        schema_name="leap_request_" + schema.lower(),
                        referenced_entity="systemuser",
                        referencing_entity=TABLE,
                        referenced_attribute="systemuserid"),
                    solution=SOLUTION)
                print("Lookup:", schema, flush=True)
        self.api("POST", "PublishXml", {
            "ParameterXml": "<importexportxml><entities><entity>leap_request</entity>"
                            "</entities></importexportxml>"})
        print("Schema ready:", info["EntitySetName"], flush=True)

    def create_roles(self):
        root = self.records("businessunit", filter="parentbusinessunitid eq null",
                            select=["businessunitid"])[0]["businessunitid"]
        privileges = self.api(
            "GET", f"EntityDefinitions(LogicalName='{TABLE}')?$select=Privileges")["Privileges"]
        metadata = {entry["PrivilegeType"]: entry["PrivilegeId"] for entry in privileges}
        user_privileges = self.api(
            "GET", "EntityDefinitions(LogicalName='systemuser')?$select=Privileges")["Privileges"]
        extra = {row["PrivilegeType"]: row["PrivilegeId"] for row in user_privileges}
        for role_name, grants in [
                ("IRS LEAP Participant", {"Create": "Basic", "Read": "Basic", "Append": "Basic"}),
                ("IRS LEAP Automation", {"Read": "Global", "Write": "Global", "Append": "Global"})]:
            role_id = self.ensure_record("role", f"name eq '{role_name}'", {
                "name": role_name,
                "businessunitid@odata.bind": f"/businessunits({root})",
            }, "roleid")
            values = [{"PrivilegeId": metadata[name], "Depth": depth}
                      for name, depth in grants.items()]
            for privilege_name in ["Read", "AppendTo"]:
                if privilege_name not in extra:
                    raise RuntimeError(f"Missing required privilege {privilege_name}")
                values.append({"PrivilegeId": extra[privilege_name], "Depth": "Global"})
            self.api("POST", f"roles({role_id})/Microsoft.Dynamics.CRM.AddPrivilegesRole", {
                "Privileges": values})
            self.add_component(role_id, 20)
            print("Role:", role_name, role_id, flush=True)

    def register_backend(self, dll_path, manifest_path):
        manifest = json.loads(Path(manifest_path).read_text(encoding="utf-8"))
        encoded_assembly = base64.b64encode(Path(dll_path).read_bytes()).decode("ascii")
        assembly_id = self.ensure_record(
            "pluginassembly", "name eq 'IrsLeap.Plugins'", {
                "name": "IrsLeap.Plugins", "version": manifest["version"],
                "culture": "neutral", "publickeytoken": manifest["publicKeyToken"],
                "sourcetype": 0, "isolationmode": 2,
                "content": encoded_assembly,
            }, "pluginassemblyid")
        self.client.records.update("pluginassembly", assembly_id, {"content": encoded_assembly})
        self.add_component(assembly_id, 91)
        plugin_types = {}
        for name in ["RequestGuard", "RequestShare", "AnnualTotal"]:
            typename = manifest["namespace"] + "." + name
            plugin_types[name] = self.ensure_record(
                "plugintype", f"typename eq '{typename}'", {
                    "typename": typename, "name": typename,
                    "friendlyname": "IRS LEAP " + name,
                    "pluginassemblyid@odata.bind": f"/pluginassemblies({assembly_id})",
                }, "plugintypeid")
        for name, message, stage, cls in [
                ("IRS LEAP - Validate create", "Create", 20, "RequestGuard"),
                ("IRS LEAP - Protect update", "Update", 20, "RequestGuard"),
                ("IRS LEAP - Block assignment", "Assign", 20, "RequestGuard"),
                ("IRS LEAP - Share request", "Create", 40, "RequestShare")]:
            message_id = self.records(
                "sdkmessage", filter=f"name eq '{message}'", select=["sdkmessageid"])[0]["sdkmessageid"]
            filter_id = self.records(
                "sdkmessagefilter",
                filter=f"primaryobjecttypecode eq '{TABLE}' and _sdkmessageid_value eq {message_id}",
                select=["sdkmessagefilterid"])[0]["sdkmessagefilterid"]
            step_id = self.ensure_record(
                "sdkmessageprocessingstep", f"name eq '{name}'", {
                    "name": name, "stage": stage, "mode": 0, "rank": 1,
                    "supporteddeployment": 0,
                    "sdkmessageid@odata.bind": f"/sdkmessages({message_id})",
                    "sdkmessagefilterid@odata.bind": f"/sdkmessagefilters({filter_id})",
                    "eventhandler_plugintype@odata.bind": f"/plugintypes({plugin_types[cls]})",
                }, "sdkmessageprocessingstepid")
            if message == "Update":
                self.ensure_record(
                    "sdkmessageprocessingstepimage",
                    f"_sdkmessageprocessingstepid_value eq {step_id} and name eq 'Before'", {
                        "name": "Before", "entityalias": "Before", "imagetype": 0,
                        "messagepropertyname": "Target",
                        "sdkmessageprocessingstepid@odata.bind":
                            f"/sdkmessageprocessingsteps({step_id})",
                    }, "sdkmessageprocessingstepimageid")
            self.add_component(step_id, 92)
        request_privileges = self.api(
            "GET", f"EntityDefinitions(LogicalName='{TABLE}')?$select=Privileges")["Privileges"]
        read_privilege = next(row["Name"] for row in request_privileges
                              if row["PrivilegeType"] == "Read")
        api_id = self.ensure_record(
            "customapi", "uniquename eq 'leap_GetAnnualTotal'", {
                "uniquename": "leap_GetAnnualTotal",
                "name": "leap_GetAnnualTotal",
                "displayname": "Get LEAP Annual Total",
                "description": "Returns approved hours only for the requester or their assigned approvers.",
                "bindingtype": 0, "isfunction": False, "isprivate": False,
                "allowedcustomprocessingsteptype": 0,
                "workflowsdkstepenabled": True,
                "executeprivilegename": read_privilege,
                "PluginTypeId@odata.bind": f"/plugintypes({plugin_types['AnnualTotal']})",
            }, "customapiid")
        for name, kind in [("RequesterId", 10), ("CalendarYear", 7)]:
            self.ensure_record(
                "customapirequestparameter",
                f"_customapiid_value eq {api_id} and uniquename eq '{name}'", {
                    "uniquename": name, "name": name, "displayname": name,
                    "type": kind, "isoptional": False,
                    "CustomAPIId@odata.bind": f"/customapis({api_id})",
                }, "customapirequestparameterid")
        for name, kind in [
                ("TotalHours", 7), ("MissingHours", 7),
                ("ApprovedRequests", 7), ("RequesterName", 10)]:
            self.ensure_record(
                "customapiresponseproperty",
                f"_customapiid_value eq {api_id} and uniquename eq '{name}'", {
                    "uniquename": name, "name": name, "displayname": name, "type": kind,
                    "CustomAPIId@odata.bind": f"/customapis({api_id})",
                }, "customapiresponsepropertyid")
        api_metadata = self.api(
            "GET", "EntityDefinitions(LogicalName='customapi')?$select=ObjectTypeCode")
        self.add_component(api_id, api_metadata["ObjectTypeCode"])
        self.api("POST", "PublishAllXml", {})
        print("Backend and annual-total API registered.", flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--environment", required=True)
    parser.add_argument("--phase", choices=["schema", "backend"], required=True)
    parser.add_argument("--dll")
    parser.add_argument("--manifest")
    args = parser.parse_args()
    if args.environment.rstrip("/") != URL:
        raise SystemExit("Explicit environment does not match .env; refusing to proceed.")
    if not URL.startswith("https://") or "/" in URL.removeprefix("https://"):
        raise SystemExit("DATAVERSE_URL must be an HTTPS organization origin without a path.")
    provisioner = Provisioner()
    provisioner.create_solution()
    if args.phase == "schema":
        provisioner.create_schema()
        provisioner.create_roles()
    else:
        if not args.dll or not args.manifest:
            parser.error("--dll and --manifest are required for backend registration.")
        provisioner.register_backend(args.dll, args.manifest)


if __name__ == "__main__":
    main()
