# Setup and Publish

## 1. Prerequisites

- Windows 10/11
- .NET 8 SDK (x64)

確認:

```powershell
dotnet --info
```

`.NET SDKs installed` に 8.x が表示されること。

## 2. Build

```powershell
dotnet restore .\src\WinPomTimer\WinPomTimer.csproj
dotnet build .\src\WinPomTimer\WinPomTimer.csproj -c Release
```

## 3. Publish Single EXE

```powershell
dotnet publish .\src\WinPomTimer\WinPomTimer.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  /p:PublishSingleFile=true `
  /p:ReadyToRun=true `
  /p:PublishTrimmed=false
```

生成先:

- `src/WinPomTimer/bin/Release/net8.0-windows/win-x64/publish/WinPomTimer.exe`

## 4. Distribution Notes

- 未署名 exe は SmartScreen 警告が出る場合がある。
- 社内配布や一般配布を想定するならコード署名を推奨。

## 5. What to Share

配布時は `publish` フォルダを丸ごと渡す。

- `src/WinPomTimer/bin/Release/net8.0-windows/win-x64/publish/`

最低限必要なファイル:

- `WinPomTimer.exe`
- `D3DCompiler_47_cor3.dll`
- `PenImc_cor3.dll`
- `PresentationNative_cor3.dll`
- `vcruntime140_cor3.dll`
- `wpfgfx_cor3.dll`
- `秒針.wav`
- `鳩時計1.wav`

注記:

- `WinPomTimer.pdb` はデバッグ用のため、通常配布には不要。
- `README_使い方.txt` は実行に必須ではないが、配布時は同梱を推奨。

## 6. ZIP Packaging Example

```powershell
$publish = ".\src\WinPomTimer\bin\Release\net8.0-windows\win-x64\publish"
$zip = ".\downloads\WinPomTimer-win-x64.zip"

Compress-Archive `
  -Path @(
    "$publish\WinPomTimer.exe",
    "$publish\D3DCompiler_47_cor3.dll",
    "$publish\PenImc_cor3.dll",
    "$publish\PresentationNative_cor3.dll",
    "$publish\vcruntime140_cor3.dll",
    "$publish\wpfgfx_cor3.dll",
    "$publish\秒針.wav",
    "$publish\鳩時計1.wav",
    "$publish\README_使い方.txt"
  ) `
  -DestinationPath $zip `
  -Force
```

## 7. Release Upload

- GitHub Releases の Assets に `downloads/WinPomTimer-win-x64.zip` をアップロードする
- README のダウンロード導線は Releases を案内先にする

## 8. End-User Manual

配布物に以下を同梱することを推奨。

- `docs/user-guide-ja.txt`
