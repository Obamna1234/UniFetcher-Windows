using Nickvision.Desktop.Keyring;
using Nickvision.Desktop.System;
using Nickvision.Parabolic.Shared.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Nickvision.Parabolic.Shared.Services;

public interface IYtdlpExecutableService : IDependencyExecutableService
{
    IReadOnlyList<string> GetDiscoveryProcessArguments(Uri url, Credential? credential);
    IReadOnlyList<string> GetDiscoveryProcessArguments(Uri url, Credential? credential, string? cookieProfile);
    Process GetDownloadProcess(DownloadOptions downloadOptions);
}
