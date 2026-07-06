using System;
using System.Threading.Tasks;
using LumenAuth;

// A minimal desktop guard: init, log in with a license key, then gate the app
// on a valid session. Run with `dotnet run` from this folder.
internal static class Program
{
    private static async Task Main()
    {
        using var auth = new LumenAuthClient(
            name: "My app",
            ownerId: "aBcD3fGh1J", // from your LumenAuth dashboard (Manage Apps)
            version: "1.0",
            secret: "lumsk_...",   // app secret from Manage Apps
            hwid: Hwid.Get());     // optional device lock

        try
        {
            await auth.InitAsync(); // resolve the app + check the version

            // Log in with just a license key. You could also call
            //   await auth.RegisterAsync(email, password, key);
            //   await auth.LoginAsync(email, password);
            await auth.LicenseAsync("LUMEN-XXXXX-XXXXX-XXXXX-XXXXX");

            if (!await auth.CheckAsync())
            {
                Console.WriteLine("Session is not valid — exiting.");
                return;
            }

            Console.WriteLine($"Welcome, {auth.User.Email} (level {auth.User.Level})");

            // Read a level-gated server variable (keep secrets off the client):
            //   string endpoint = await auth.VarAsync("api-endpoint");

            // Download a license-gated file:
            //   byte[] update = await auth.FileAsync("<file-id>");

            Console.WriteLine("Licensed — starting the app...");
        }
        catch (LumenAuthException ex)
        {
            Console.WriteLine($"LumenAuth error ({ex.Status}): {ex.Message}");
        }
    }
}
