# Win Pom Timer Architecture

## 1. Runtime and Packaging

- Framework: .NET 8
- UI: WPF
- Target: Windows x64
- Packaging: Self-contained single-file exe (publish profile)

## 2. Project Layout

```text
src/WinPomTimer/
  Domain/
  Services/
  App.xaml
  MainWindow.xaml
```

## 3. Main Components

- `PomodoroTimerService`
  - タイマー状態機械とカウントダウン処理を管理。
  - Tick/StateChanged/SessionCompleted イベントを発行。
- `AudioService`
  - 通知音、開始音、事前通知音、秒針音を再生。
- `SettingsService`
  - JSON で設定を保存・復元。
- `AppStateService`
  - 実行中モード、残り時間、サイクル数、直近状態を保存・復元。
- `SessionLogService`
  - セッションログを JSONL で追記保存。
- `ExportService`
  - JSON / CSV / ICS 出力を生成。

## 4. Data Files

保存先: `%AppData%/WinPomTimer`

- `settings.json`
- `state.json`
- `sessions.jsonl`

## 5. UI Composition

- MainWindow
  - モード表示
  - 残り時間
  - 操作ボタン
  - 設定パネル
  - メモ入力欄
- Tray Menu
  - Show
  - Start/Pause
  - Skip
  - Exit

## 6. Failure Policy

- 設定ファイル破損時はデフォルトへフォールバック。
- 音ファイルが無効でもアプリは継続。
- ログ保存失敗時は UI で通知し、次回保存で再試行。

