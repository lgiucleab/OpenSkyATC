# Third-party notices

OpenSkyATC is licensed under GPL-3.0. This file lists every third-party component, model, voice and dataset, with its licence and whether it is shipped to users.

## Test-only dependencies (not shipped)

| Component | Version | Licence | Source |
| --- | --- | --- | --- |
| xunit | 2.9.3 | Apache-2.0 | https://github.com/xunit/xunit |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 | https://github.com/xunit/visualstudio.xunit |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | https://github.com/microsoft/vstest |
| coverlet.collector | 6.0.4 | MIT | https://github.com/coverlet-coverage/coverlet |

Apache-2.0 and MIT are compatible with GPL-3.0.

## Shipped dependencies

None yet.

## Required but not shipped (user supplies)

| Component | Licence | Notes |
| --- | --- | --- |
| Microsoft.FlightSimulator.SimConnect.dll, SimConnect.dll (MSFS 2024 SDK) | Microsoft Flight Simulator SDK EULA (proprietary) | Not in the repo or releases. Loaded at runtime from the user's SDK install. Linking is allowed by `LICENSE-EXCEPTION.md`. |
