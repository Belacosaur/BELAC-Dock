# Third-party notices

Third-party names and logos do not imply affiliation or endorsement. The project's original code is MIT licensed; see LICENSE.

## Provider marks

SVG provider marks and derived WPF geometry come from [LobeHub lobe-icons](https://github.com/lobehub/lobe-icons), Copyright (c) 2023 LobeHub, MIT. The original license remains at `src/Belac.Dock.App/Assets/Providers/LICENSE` and is copied unchanged into binary releases as `licenses/provider-icons-LICENSE.txt`. Provider marks remain trademarks of their respective owners.

## Runtime and libraries

.NET / Windows Desktop runtime, Microsoft.Extensions.* and related Microsoft packages are Microsoft and contributors, MIT. Self-contained releases retain runtime license and third-party notice files under `licenses/microsoft.*.runtime.win-x64-*`. CommunityToolkit.Mvvm is .NET Foundation and contributors, MIT.

Test tooling is not shipped with the app: xUnit.net (Apache-2.0), Microsoft.NET.Test.Sdk (MIT), xunit.runner.visualstudio (MIT), coverlet.collector (MIT).

Binary packaging copies available NuGet license files into `licenses/packages`. `dotnet list BELAC.Dock.sln package --include-transitive` lists resolved versions. Packages retain upstream ownership and terms.

Windows SDK/COM interfaces are used for interoperability. The optional Shell resolver ABI was checked against the System Informer interface declaration; no System Informer implementation is bundled.


## OpenTokenUsage provider protocol mappings

Native Codex, Claude and Cursor usage mappings are adapted from [OpenTokenUsage](https://github.com/PowerUserZ/OpenTokenUsage). The companion is optional and is not bundled.

MIT License

Copyright (c) 2026 Robin Ebers
Copyright (c) 2026 PowerUserZ

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
