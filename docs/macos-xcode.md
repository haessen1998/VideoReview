# macOS Xcode / MacCatalyst workload setup

Windows and macOS development machines can use different .NET SDK patch versions. Do not pin the repository with a root `global.json` unless every machine has the same workload set installed.

For Xcode 26.3, install/update the .NET workloads on the Mac to workload set `10.0.202`, which includes MacCatalyst support for Xcode 26.3.

```bash
sudo xcode-select --switch /Applications/Xcode.app/Contents/Developer
xcodebuild -version

sudo dotnet workload config --update-mode workload-set
sudo dotnet workload update --version 10.0.202 --source https://api.nuget.org/v3/index.json
sudo dotnet workload restore --source https://api.nuget.org/v3/index.json
```

If `workload update` does not install the MAUI workloads, install them explicitly:

```bash
sudo dotnet workload install maui maccatalyst --version 10.0.202 --source https://api.nuget.org/v3/index.json
```

Then build the MacCatalyst target:

```bash
dotnet build VideoReview/VideoReview/VideoReview.csproj -f net10.0-maccatalyst
```
