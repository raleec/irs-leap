IRS LEAP
========

Leave and Exception Approval Portal: a solutions-first Canvas app, Dataverse,
and Power Automate workshop.

This is a training/prototype project, not an official IRS product or a
production accreditation. Use synthetic data. Customer GCC compatibility and
ordinary-user deployment rehearsal remain required.

Release 1.1.0.0
--------------

The complete component package contains:

* A native Canvas app with request entry, authorized calendar, details and annual totals.
* A first-responder approval cloud flow, exported stopped.
* The Dataverse request table, 13 category choices, roles, plug-ins and annual-total API.
* Three unbound connection references and two consumed environment-variable definitions.

The source workflow is not enabled, and no notification or trial enrollment is
performed by these artifacts. Authenticated connections, target settings,
licenses, roles and app sharing are deployment responsibilities.

Start here
----------

* `Workshop overview <canvas-starter/START-HERE.txt>`_
* `150-minute solutions-first facilitator script <canvas-starter/guide/SOLUTIONS-FIRST.txt>`_
* `Release inventory and configuration <canvas-starter/guide/RELEASE-1.1.txt>`_
* `Searchable HTML guide <canvas-starter/guide/IRS-LEAP-Workshop-Guide.html>`_
  (download/open locally).

Import a solution
-----------------

For an isolated authoring environment, import
``canvas-starter/deliverables/IRSLEAPCanvasStarter_1_1_0_0_unmanaged.zip``.

For a different downstream target, use the corresponding ``managed.zip``.
Do not import both flavors over the same solution in one environment.

The outer ``IRS-LEAP-Canvas-Hackathon-Kit.zip`` is a delivery archive, not the
file to select in the Power Apps solution-import wizard.

Bind target connections to the actual references:

* ``leap_LEAPDataverse``
* ``leap_LEAPApprovals``
* ``leap_LEAPOutlook``

Supply current values for ``leap_AppPlayUrl`` and ``leap_OperationsMailbox``.
Use the target app's real published Web link, not a source-environment link.
Keep the flow off until connection ownership, licensing, data roles, sharing,
synthetic recipients and notification templates have been reviewed.

Behavior
--------

Each request has one date, one category and optional integer leave hours
from 0 through 24. Multiple requests may overlap. Totals sum entered hours
on approved requests for the year of leave, counting overlapping requests
independently. Blank hours remain distinct from explicit zero.

Requesters and assigned approvers can obtain authorized annual aggregates
without being granted access to unrelated request details. The current
approver-to-requester aggregate authorization has no automatic expiry; review
that policy before real-data deployment.

Source layout
-------------

* ``canvas-starter/runtime-canvas``: native app YAML, actual .msapp and evidence boundary.
* ``canvas-starter/runtime-flow``: disabled flow definition, target configuration mapping and reports.
* ``canvas-starter/backend``: signed plug-in source, build artifact and local validation harness.
* ``canvas-starter/solution-source``: actual Dataverse-exported solution source.
* ``canvas-starter/guide``: facilitator guides and copy/paste formulas.
* ``canvas-starter/deliverables``: latest managed/unmanaged solutions and workshop kit.

Publication boundaries
----------------------

This repository is a fresh latest-release snapshot, not the original development
Git history. Unrelated Code App scaffolding, local-only environment inspection
scripts, credentials, token caches, private signing keys and build caches are
excluded. Human-readable guides omit the development tenant and organization URL.

The native Power Platform artifacts and exported solution source remain
byte-preserved to avoid damaging their importability. They retain non-secret
component identifiers and source-environment binding metadata. Imports must bind
to the intended target environment. No real leave records are supplied.

Operational scope
-----------------

The standalone development player was blocked by its owner's missing runtime
plan; no trial was started. App authoring and Studio scenarios do not establish
published ordinary-user or customer GCC readiness. The unbound approval flow
requires authorized connection binding and native approval/email rehearsal.

No production HR/payroll integration, leave-balance accounting, full durable
outbox, or exactly-once notification guarantee is claimed.
