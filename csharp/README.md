# LumenAuth — C# SDK

License auth, HWID locking and license-gated downloads for **.NET desktop apps**
(WinForms, WPF, console, Unity). The library targets **netstandard2.0**, so it
runs on .NET Framework 4.6.1+, .NET Core 2.0+, .NET 5–8 and Mono.

Bind your app with three values — **name + ownerid + version** — no URL and no
secret in your code.

## Install

Reference the project directly:

```xml
<ProjectReference Include="path/to/sdks/csharp/LumenAuth/LumenAuth.csproj" />
```

or drop the two files (`LumenAuthClient.cs`, `Hwid.cs`) straight into your
project. The only dependency is the `System.Text.Json` NuGet package (already
built in on .NET Core 3.0+ / .NET 5+).

## Usage

```csharp
using LumenAuth;

using var auth = new LumenAuthClient(
    name: "My app",
    ownerId: "aBcD3fGh1J",   // from your LumenAuth dashboard
    version: "1.0",
    hwid: Hwid.Get());       // optional device lock

await auth.InitAsync();                              // resolve app + check version
await auth.LicenseAsync("LUMEN-XXXXX-XXXXX-XXXXX");  // or Register / Login

if (!await auth.CheckAsync())
    Environment.Exit(1);

Console.WriteLine($"Level {auth.User.Level}");

string secret = await auth.VarAsync("api-endpoint"); // level-gated variable
byte[] update = await auth.FileAsync("<file-id>");   // license-gated download
```

Every call throws `LumenAuthException` on failure; its `Status` holds the HTTP
status code (`401`/`403`/`404`/`409`/`426`). A version mismatch at `InitAsync`
throws with status `426` (update required).

## API

| Method | Purpose |
| --- | --- |
| `InitAsync()` | Resolve the app + check the version. Call once at startup. |
| `RegisterAsync(email, password, license)` | Redeem a license → create a user. |
| `LoginAsync(email, password)` | Email + password (HWID-locked). |
| `LicenseAsync(license)` | Log in with just a license key. |
| `CheckAsync()` | `true` if the session is still valid. |
| `VarAsync(name)` | Read a level-gated server variable. |
| `FileAsync(fileId)` | Download a license-gated file (bytes). |
| `Logout()` | Forget the session locally. |

`Session`, `Token` and `User` are exposed as properties after a successful auth
call.

## Run the example

```bash
cd sdks/csharp/Example
dotnet run
```
