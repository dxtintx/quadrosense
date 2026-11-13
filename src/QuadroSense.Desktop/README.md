# QuadroSense desktop

Publish a standalone Windows x64 executable from the repository root:

```powershell
dotnet publish src/QuadroSense.Desktop/QuadroSense.Desktop.csproj -p:PublishProfile=SingleExe
```

Distribute `dist/QuadroSense.Desktop.exe`. It includes .NET, the Windows App SDK,
and the API. Double-click it to open the desktop and start the local API in the
background, with no console window. Bundled runtime files extract automatically
on first launch. No separate API process or .NET installation is needed.

Run `dotnet run` from this directory. The desktop app starts the local API at
`http://localhost:5176` when it is unavailable. If that API is already running,
the desktop app reuses it. Closing the window stops only the API instance owned
by that desktop process.

Settings lets you choose another API address. The address is saved in
`%LocalAppData%\QuadroSense\desktop.json`. Custom API instances must be started
separately.

Trigger, haptics and lighting editors load the service's current configuration.
Apply sends changes immediately. A new profile captures the current setup;
**Save current setup** updates an existing profile's triggers, haptics, lighting
and bindings while preserving its tuning and process associations. Tuning saves
directly to the active profile. Profile activation restores audio configuration
and bindings along with controller output settings.

The UI supports monochrome light and dark themes. Hardware lightbar RGB values
still control the controller's physical LEDs.
