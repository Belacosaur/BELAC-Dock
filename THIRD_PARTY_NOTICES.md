# Third-party notices

Third-party names and logos do not imply affiliation or endorsement. The project's original code is MIT licensed; see LICENSE.

## Provider marks

SVG provider marks and derived WPF geometry come from [LobeHub lobe-icons](https://github.com/lobehub/lobe-icons), Copyright (c) 2023 LobeHub, MIT. The original license remains at `src/Belac.Dock.App/Assets/Providers/LICENSE` and is copied unchanged into binary releases as `licenses/provider-icons-LICENSE.txt`. Provider marks remain trademarks of their respective owners.

## Runtime and libraries

.NET / Windows Desktop runtime, Microsoft.Extensions.* and related Microsoft packages are Microsoft and contributors, MIT. Self-contained releases retain runtime license and third-party notice files under `licenses/microsoft.*.runtime.win-x64-*`. CommunityToolkit.Mvvm is .NET Foundation and contributors, MIT.

Test tooling is not shipped with the app: xUnit.net (Apache-2.0), Microsoft.NET.Test.Sdk (MIT), xunit.runner.visualstudio (MIT), coverlet.collector (MIT).

Binary packaging copies available NuGet license files into `licenses/packages`. `dotnet list BELAC.Dock.sln package --include-transitive` lists resolved versions. Packages retain upstream ownership and terms.

Windows SDK/COM interfaces are used for interoperability. The optional Shell resolver ABI was checked against the System Informer interface declaration; no System Informer implementation is bundled.
