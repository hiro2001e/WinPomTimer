# downloads

このフォルダは、配布用 ZIP の作成先です。

## ルール

- 配布ファイル名は `WinPomTimer-win-x64.zip` を基本にする
- 実際の一般公開は GitHub Releases の Assets にアップロードする

## 作成コマンド（PowerShell）

```powershell
Compress-Archive `
  -Path .\src\WinPomTimer\bin\Release\net8.0-windows\win-x64\publish\* `
  -DestinationPath .\downloads\WinPomTimer-win-x64.zip `
  -Force
```
