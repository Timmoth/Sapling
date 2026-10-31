# Sapling Mobile

Native .NET MAUI app for Android and iOS. The app references `Sapling.Engine` directly and
runs searches locally on the device. It has no WebView, backend dependency, or network
permission.

## Build

Install the .NET MAUI workload for the SDK pinned by the repository, then build a platform:

```sh
dotnet workload install maui
dotnet build src/Sapling.Mobile/Sapling.Mobile.csproj -c Release -f net10.0-android
dotnet build src/Sapling.Mobile/Sapling.Mobile.csproj -c Release -f net10.0-ios
```

The iOS build requires Xcode. Device signing is configured by the local Xcode environment.

The app starts a single engine worker with a 16 MiB transposition table. Search is driven in
bounded slices so the UI stays responsive and can show depth, score, time, nodes, speed, and
the current principal variation. The board, game controls, analysis, and export actions are
native MAUI views.
