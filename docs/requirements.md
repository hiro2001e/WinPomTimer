# Win Pom Timer Requirements

## 1. Scope

Windows で動作するポモドーロタイマーを提供する。MVP ではタイマー運用、通知、音、トレイ常駐、ログ保存、基本エクスポートまでを対象とする。

## 2. Functional Requirements

### 2.1 Timer

- 作業時間の初期値は 25 分。変更可能。
- 短休憩時間の初期値は 5 分。変更可能。
- 長休憩は N サイクルごとに発生。N は変更可能。
- 長休憩時間は変更可能。
- タイマー操作: 開始、一時停止、再開、スキップ、リセット。
- 自動遷移設定:
  - 作業終了後に休憩を自動開始するか。
  - 休憩終了後に作業を自動開始するか。

### 2.2 Sound and Notification

- セッション切替時通知音。
- 作業開始通知音。
- 休憩終了前の事前通知音 (`n` 秒前)。
- 作業中の秒針音。
- 音量、ミュート、音源選択。
- 秒針音設定:
  - ON/OFF
  - 音量
  - 音源選択
  - 再生モード (作業中のみ / 全モード)

### 2.3 UI and Window Behavior

- 小型メイン画面でモードと残り時間を表示。
- 開始/停止/スキップ/リセットを操作可能。
- テーマ色、透過、最前面設定を提供。
- マウス離脱時に透過できる。

### 2.4 Tray Behavior

- タスクトレイ常駐。
- ウィンドウの `X` は終了ではなくトレイ最小化。
- トレイメニュー:
  - 表示
  - 開始/一時停止
  - スキップ
  - 終了
- 終了時は確認ダイアログを表示。
- スタートアップ登録は実装しない。

### 2.5 Logs, Stats, Export

- セッション単位でメモを保存。
- 日次/月次の作業時間を集計可能。
- JSON / CSV / ICS 形式でエクスポート。

## 3. State Model

- States: `IDLE`, `WORK`, `SHORT_BREAK`, `LONG_BREAK`, `PAUSED`
- Events: `START`, `PAUSE`, `RESUME`, `SKIP`, `COMPLETE`, `RESET`, `CLOSE_TO_TRAY`, `EXIT`

Transition policy:

1. `IDLE + START -> WORK`
2. `WORK + COMPLETE -> SHORT_BREAK` (長休憩条件で `LONG_BREAK`)
3. `SHORT_BREAK/LONG_BREAK + COMPLETE -> WORK`
4. `WORK/SHORT_BREAK/LONG_BREAK + PAUSE -> PAUSED`
5. `PAUSED + RESUME -> previous active state`
6. `Any + SKIP -> next session`
7. `Any + RESET -> IDLE`
8. `Any + CLOSE_TO_TRAY -> keep current state`
9. `Any + EXIT -> terminate after confirmation`

## 4. Non-Functional

- タイマー状態を定期保存する。
- 異常終了後に可能な限り復元する。
- 音再生失敗時もタイマー継続。

