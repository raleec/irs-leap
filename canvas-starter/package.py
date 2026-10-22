"""Export the real Dataverse solution and assemble the workshop delivery kit."""

import argparse
import json
from pathlib import Path
import subprocess
from zipfile import ZipFile, ZIP_DEFLATED
import xml.etree.ElementTree as ET
import os
import re

ROOT = Path(__file__).resolve().parent
SOLUTION = "IRSLEAPCanvasStarter"


def run(*args):
    subprocess.run(list(args), check=True)


def inspect_solution(path, managed, require_app_and_flow=False):
    with ZipFile(path) as archive:
        if archive.testzip() is not None:
            raise RuntimeError(f"Invalid archive: {path}")
        names = set(archive.namelist())
        for required in ["solution.xml", "customizations.xml", "[Content_Types].xml"]:
            if required not in names:
                raise RuntimeError(f"{path.name} lacks {required}")
        solution = ET.fromstring(archive.read("solution.xml"))
        unique_name = solution.findtext(".//UniqueName")
        if unique_name != SOLUTION:
            raise RuntimeError(f"Unexpected solution identity: {unique_name}")
        if solution.findtext(".//Managed") != ("1" if managed else "0"):
            raise RuntimeError(f"Incorrect managed flag in {path.name}")
        customizations = archive.read("customizations.xml").decode("utf-8")
        customization_tree = ET.fromstring(customizations)
        for expected in ["leap_request", "IRS LEAP Participant", "IRS LEAP Automation"]:
            if expected not in customizations:
                raise RuntimeError(f"{path.name} lacks required component {expected}")
        plugin_files = [name for name in names if name.lower().endswith(".dll")]
        if not plugin_files:
            raise RuntimeError(f"No plugin binary in {path.name}")
        if not any("leap_GetAnnualTotal" in name for name in names) and "leap_GetAnnualTotal" not in customizations:
            raise RuntimeError(f"Annual total API absent from {path.name}")
        canvas_apps = sorted(name for name in names
                             if name.lower().startswith("canvasapps/")
                             and name.lower().endswith(".msapp"))
        cloud_flows = sorted(name for name in names
                             if name.lower().startswith("workflows/")
                             and name.lower().endswith(".json"))
        references = sorted(
            item.attrib["connectionreferencelogicalname"]
            for item in customization_tree.findall(".//connectionreference")
            if "connectionreferencelogicalname" in item.attrib)
        variable_files = sorted(
            name for name in names if name.startswith("environmentvariabledefinitions/")
            and name.endswith("/environmentvariabledefinition.xml"))
        variable_names = sorted(name.split("/")[1] for name in variable_files)
        missing = []
        if not canvas_apps:
            missing.append("Canvas app")
        if not cloud_flows:
            missing.append("cloud approval flow")
        if require_app_and_flow and missing:
            raise RuntimeError(
                f"{path.name} is not a complete runtime release; missing: "
                + ", ".join(missing))
        if require_app_and_flow:
            for required in ["leap_LEAPDataverse", "leap_LEAPApprovals", "leap_LEAPOutlook"]:
                if required not in references:
                    raise RuntimeError(f"Full release lacks connection reference {required}.")
            for required in ["leap_AppPlayUrl", "leap_OperationsMailbox"]:
                if required not in variable_names:
                    raise RuntimeError(f"Full release lacks environment variable {required}.")
        return {
            "file": path.name, "version": solution.findtext(".//Version"),
            "entries": len(names), "plugins": plugin_files,
            "canvasApps": canvas_apps, "cloudFlows": cloud_flows,
            "connectionReferences": references,
            "environmentVariables": variable_names,
            "missingRuntimeComponents": missing,
            "containsAppAndFlow": not missing,
            "preEventRehearsalRequired": True,
        }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--pac")
    parser.add_argument("--environment")
    parser.add_argument("--release-version",
                        help="Four-part solution version; defaults to source version or last local report.")
    parser.add_argument("--local-only", action="store_true",
                        help="Rebuild the kit from existing exports without accessing Dataverse.")
    parser.add_argument("--require-app-and-flow", action="store_true",
                        help="Reject a backend-only export for the short end-to-end workshop.")
    args = parser.parse_args()
    sys_path = ROOT / "scripts" if (ROOT / "scripts").exists() else ROOT.parent / "scripts"
    configured_target = None
    release_version = args.release_version
    if not args.local_only:
        if not args.pac or not args.environment:
            parser.error("--pac and --environment are required unless --local-only is used.")
        import sys
        sys.path.insert(0, str(sys_path))
        from auth import get_credential, load_env
        from PowerPlatform.Dataverse.client import DataverseClient
        load_env()
        configured_target = os.environ.get("DATAVERSE_URL", "").rstrip("/")
        if not configured_target or args.environment.rstrip("/") != configured_target:
            parser.error("--environment must match the locally approved DATAVERSE_URL.")
        with DataverseClient(configured_target, get_credential()) as client:
            solutions = [
                row for page in client.records.get(
                    "solution", filter=f"uniquename eq '{SOLUTION}'", select=["version"])
                for row in page]
        if len(solutions) != 1:
            parser.error("Expected exactly one source solution.")
        actual_version = solutions[0]["version"]
        if release_version and release_version != actual_version:
            parser.error("--release-version does not match the source solution; no export attempted.")
        release_version = actual_version
    output = ROOT / "deliverables"
    output.mkdir(exist_ok=True)
    if not release_version:
        prior_report = output / "package-report.json"
        if not prior_report.exists():
            parser.error("--local-only needs --release-version or an existing package report.")
        release_version = json.loads(prior_report.read_text(encoding="utf-8"))["exports"][0]["version"]
    if not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", release_version):
        parser.error("Release version must contain four integer components.")
    report = []
    packages = []
    for managed in [False, True]:
        path = output / (
            "IRSLEAPCanvasStarter_" + release_version.replace(".", "_") + "_" +
            ("managed" if managed else "unmanaged") + ".zip")
        if not args.local_only:
            run(args.pac, "solution", "export", "--name", SOLUTION,
                "--path", str(path), "--managed", str(managed).lower(),
                "--environment", configured_target, "--overwrite")
        inventory = inspect_solution(path, managed, args.require_app_and_flow)
        if inventory["version"] != release_version:
            raise RuntimeError(f"{path.name} does not contain the requested version.")
        report.append(inventory)
        packages.append(path)
    if not args.local_only:
        run(args.pac, "solution", "unpack", "--zipfile", str(packages[0]),
            "--folder", str(ROOT / "solution-source"), "--packagetype", "Unmanaged",
            "--allowWrite", "--allowDelete")
    (output / "package-report.json").write_text(
        json.dumps({
            "solution": SOLUTION,
            "workshopMode": "solutions-first; target configuration and rehearsal required",
            "exports": report,
        }, indent=2), encoding="utf-8")
    kit = output / "IRS-LEAP-Canvas-Hackathon-Kit.zip"
    with ZipFile(kit, "w", ZIP_DEFLATED) as archive:
        for directory in [
                ROOT / "guide", ROOT / "backend", ROOT / "solution-source",
                ROOT / "runtime-canvas"]:
            for path in directory.rglob("*"):
                if not path.is_file():
                    continue
                if any(part in {
                        "bin", "obj", "node_modules", "__pycache__", ".keys",
                        ".packages", ".nuget-http-cache", ".nuget-scratch", ".dotnet-home"
                } for part in path.relative_to(ROOT).parts):
                    continue
                if path.suffix in {".snk", ".pfx", ".user"}:
                    continue
                if directory.name == "runtime-canvas" and not (
                        path.name.endswith(".pa.yaml") or path.suffix == ".msapp"
                        or path.name == "runtime-validation.json"):
                    continue
                archive.write(path, path.relative_to(ROOT))
        flow_files = [
            "deploy_disabled_flow.py", "leap-first-response.clientdata.json",
            "runtime-flow-report.json", "runtime-flow-report.txt",
            "syntax-validation.json", "deployment-settings.json",
            "independent-review.json", "independent-review.txt",
        ]
        for name in flow_files:
            path = ROOT / "runtime-flow" / name
            if path.exists():
                archive.write(path, "runtime-flow/" + name)
        for path in [
                *packages, ROOT / "START-HERE.txt", ROOT / "ADMIN-HANDOFF.txt",
                ROOT / "provision.py", ROOT / "package.py", ROOT / "render_guide.py",
                ROOT / "backend-validation.json", output / "package-report.json"]:
            archive.write(path, path.name)
        archive.write(sys_path / "auth.py", "scripts/auth.py")
    print("Workshop kit:", kit)


if __name__ == "__main__":
    main()
