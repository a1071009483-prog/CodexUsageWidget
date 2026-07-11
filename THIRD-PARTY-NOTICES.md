# Third-Party Notices

Codex Usage Widget is built with the .NET 8 Windows Desktop SDK and the direct
dependencies listed below. Each component remains subject to its own license;
including a component here does not alter its license terms.

| Component | Pinned version | License | Upstream |
| --- | ---: | --- | --- |
| .NET SDK / Runtime, WPF, and Windows Forms | 8.0.422 / .NET 8 | MIT | <https://github.com/dotnet> |
| Microsoft.Data.Sqlite | 8.0.28 | MIT | <https://github.com/dotnet/efcore> |
| System.Text.Json | 8.0.6 | MIT | <https://github.com/dotnet/runtime> |
| System.Security.Cryptography.ProtectedData | 8.0.0 | MIT | <https://github.com/dotnet/runtime> |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | <https://github.com/microsoft/vstest> |
| xunit | 2.9.3 | Apache-2.0 | <https://github.com/xunit/xunit> |
| xunit.runner.visualstudio | 2.8.2 | Apache-2.0 | <https://github.com/xunit/visualstudio.xunit> |
| coverlet.collector | 6.0.4 | MIT | <https://github.com/coverlet-coverage/coverlet> |

The full license text and copyright information for each NuGet package are
available from its upstream repository and from the package metadata restored by
NuGet. Transitive dependencies distributed with these packages retain their own
notices and license terms.

## Runtime prerequisites

- Windows 10 version 2004 (build 19041) or later, or Windows 11.
- The .NET 8 Windows Desktop Runtime for framework-dependent distribution.
- The Codex CLI and an eligible signed-in Codex account for App Server features;
  the widget does not copy or persist Codex authentication material.

Developers use the repository-local .NET SDK pinned by `global.json` to version
8.0.422. Normal application use must not require administrator privileges.
