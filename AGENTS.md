# Soroe

このリポジトリを触る人とコーディング支援ツールが最初に読む文書。毎回の作業で必要な
原則・規約・コマンドだけを置き、理由や経緯の詳細は `docs/` と各スクリプトの冒頭コメントに分けてある。

<!--
保守メモ
- ここに足すのは「無いと毎回の作業で間違えること」だけ。1 行ごとに「消したら間違えるか」で判断する。
  経緯・実測値・手順の詳細は docs/ かスクリプトの冒頭へ置き、ここには参照だけを残す
- 強調（太字）は本当に外せない規則だけに使う。多用すると、どれも目立たなくなる
- 「この文書が読み込まれているか確かめる」の答えをここに書かない。書くとこの節を読むだけで答えられてしまう
- 設問は仕組みを問う形にする。「どの調整項目が」のように UI の構成に依存する設問は、項目の増減で
  成立しなくなる（グレースケールと二値化を UI から外したとき、実際に 2 問目が成立しなくなった）
- 見出し「構成とビルド」は src/・tools/・docs/ から参照されている。名前を変えるなら参照元も直す
-->

## 文書の地図

| 文書 | 何が書いてあるか | 読むとき |
|---|---|---|
| [docs/pitfalls.md](docs/pitfalls.md) | 触ると静かに壊れる箇所 | コードを変更する前に必ず |
| [docs/design.md](docs/design.md) | 設計方針・機能仕様・画面構成 | 仕様や判断の根拠を確かめるとき |
| [docs/decisions.md](docs/decisions.md) | 見送った機能と、見送った理由 | 機能を提案する前。要望に答えるとき |
| [docs/development.md](docs/development.md) | 性能の数字の扱い・作業で踏んだ落とし穴 | 性能を測るとき。スクリプトで一括作業をするとき |
| [docs/third-party-sources.md](docs/third-party-sources.md) | 同梱物のライセンス本文の出所 | 依存を上げたとき。表記を作り直すとき |

`docs/` は開発者向け。利用者向けの説明は README が担う。配布物に同梱するのは
ルートの `LICENSE` と `THIRD-PARTY-NOTICES.txt`。

### 触る前に読む

以下は、変えると**同じ画像・同じ設定でも出力が変わる**か、原則 2 に触れる。
理由と条件は参照先にだけ書いてある。

- 適用順序 … [docs/design.md](docs/design.md) の「適用順序は固定」
- `ImageRenderer.CanonicalEdge`・`ImageRenderer.SharpenSigma`・`Apply` の `stopBeforeBinarize` を置く位置
  … [docs/pitfalls.md](docs/pitfalls.md) の「出力を決める定数と、経路の一致」
- 安全ガードと実行前の再確認 … [docs/design.md](docs/design.md) の「安全ガード（必須）」
- 原則 2 に関わるテストの完成条件 … 本ファイルの「コーディング規約」

### この文書が読み込まれているか確かめる

`CLAUDE.md` は本ファイルを読み込む 1 行だけ。参照をたどらないツールや設定では、
この文書と `docs/` を読まないまま作業が始まる。作業の前に次を尋ね、答えられなければ
読ませてから始めること。

1. 適用順序の 8 番目は何か（[docs/design.md](docs/design.md) の「適用順序は固定」）
2. `CanonicalEdge` は性能の調整値ではなく「出力を決める定数」だとされている。なぜか
   （[docs/pitfalls.md](docs/pitfalls.md) の「出力を決める定数と、経路の一致」）
3. 原則 2 に関わるテストは、どこまでやったら完成か（本ファイルの「コーディング規約」）

## プロジェクト概要

画像の一括処理に特化した Windows デスクトップアプリ。多機能ソフトの「起動しても何をどうすれば
いいか分からない」を避けるため、**機能を絞り、分かりやすさに特化する**。名前は「揃える」に由来する。

- ライセンス: MIT（無償）
- 配布形態: 自己完結（self-contained）+ 単一ファイルの zip。解凍して動く（インストーラもランタイムも不要）

### 判断に迷ったときの原則

1. **分かりやすさ > 多機能**。機能追加の提案より、既存機能を分かりやすくする提案を優先する
2. **原本を絶対に破壊しない**。不可逆な操作は既定にしない
3. **設定は常にデータとして表現する**（UI に状態を持たせない）

## コーディング規約

- コメントは日本語で、「何をしているか」ではなく「なぜそうしているか」を書く
- public なクラス・メソッドには日本語の XML ドキュメントコメント（`///`）を付ける
- 識別子は英語で、C# の標準的な命名規則に従う。UI に表示する文言は日本語
- コミットメッセージは Conventional Commits 形式（`feat:` `fix:` `docs:` `chore:` など）で、説明部分は日本語
- 作業はフィーチャーブランチで行い、確認後に `main` へ `--ff-only` でマージする
- **原則 2（原本を破壊しない）に関わるテストは、意図的に壊した実装で実際に落ちることを
  確認してから完成とする。** 対象は安全ガード・同一実行内の衝突回避・実行前の再確認・
  `Overwrite` を保存対象から外していること。いずれも壊れていても普段は気づけない防御で、
  通ったことが動いている証拠にならない。テストの置き場は `tests/Soroe.Verify`
  - 実行前の再確認は表示状態（見積もりが画面に出ているか）に依存している。
    表示まわりを変更したら、この確認をやり直す（[docs/design.md](docs/design.md) の「安全ガード（必須）」）

## 構成とビルド

.NET 10 / WPF（MVVM、`CommunityToolkit.Mvvm`）/ 標準の Fluent テーマ（`ThemeMode`）/
OpenCvSharp4（`.Windows`・`.Extensions`・`.WpfExtensions`）。プラットフォームは x64 固定。

```
Soroe.slnx
├ src/Soroe/            アプリ本体（Models / Services / ViewModels / Views / Common）
│  └ Assets/            アイコンの SVG（サイズごと）と、そこから作る Soroe.ico
├ tests/Soroe.Verify/   検証ハーネス（コンソールアプリ）
└ tools/                作業用スクリプト（撮影・アイコン・配布物）
```

本体は 1 プロジェクトのまま保つ。参照の向きはフォルダ分けと `internal` で守れるうえ、
zip で配る形態では DLL が少ないほうがよい。

以下はリポジトリ直下で PowerShell から実行する。

```
dotnet run --project src/Soroe                                         # 実行
dotnet run --project tests/Soroe.Verify                                # 検証（全通過なら終了コード 0）
dotnet run --project tests/Soroe.Verify -c Release -- --bench          # 性能測定（合成画像。1 分近くかかる）
dotnet run --project tests/Soroe.Verify -c Release -- --bench 写真.jpg  # 実写で測る

powershell -ExecutionPolicy Bypass -File tools/screenshot.ps1 -Exe <exe> -Out <絶対パス.png> [-Width 720 -Height 600] [-ScrollEnd] [-Maximize]
powershell -ExecutionPolicy Bypass -File tools/make-icon.ps1                    # SVG から Soroe.ico を作る
powershell -ExecutionPolicy Bypass -File tools/pack.ps1 -Out <リポジトリ外の絶対パス>  # 配布用 zip
```

- **`.ps1` には `-ExecutionPolicy Bypass` を付ける。** 既定の `Restricted` では `.ps1` ファイルを
  実行できない。エージェントのシェルは `Bypass` で起動されていて止まらないことがあるので、
  手元で通ったことは利用者の環境で通る証拠にならない（[docs/development.md](docs/development.md)）
- 撮影: `-Width` / `-Height` は論理ピクセル。既定サイズを撮るときは省略する（既定サイズは作業領域に
  合わせて縮むことがあり、数値を渡すと確認にならない）。`-ScrollEnd` は調整パネルの末尾まで送って撮る。
  実装上の注意は [docs/pitfalls.md](docs/pitfalls.md) の「WPF と Win32」
- アイコン: 意匠は SVG を直し、`make-icon.ps1` で作った `.ico` を一緒にコミットする（ビルドは `.ico`
  しか見ない）。`.ico` を手で編集しない。サイズごとに別の絵である理由と検査の内容は、スクリプトの
  冒頭と [docs/decisions.md](docs/decisions.md) の「アプリのアイコン」
- 配布物: 版は csproj の `Version` から取る（スクリプトに数字を書かない）。検査の内容と発行設定の
  理由は `tools/pack.ps1` の冒頭

## 作業の規律

いずれも実際に踏んだもの。「気をつける」で済ませず、手順として守ること。経緯は
[docs/development.md](docs/development.md) にある。

- 変更したら必ず検証ハーネスを通す。ViewModel を直接つついて確かめる形式で、xUnit などは使わない
  （WPF の `Dispatcher` を回す検証が多く、素のコンソールアプリのほうが素直に書けるため）
- 性能に関わる変更をしたら `--bench` を走らせる。文書やコメントに数字を書くのは判断の根拠になる
  ものだけにし、測定日・条件・再現コマンドを添える。環境が違えば絶対値ではなく比率で比べる
  （[docs/development.md](docs/development.md) の「性能の数字の扱い」）
- 作業用ファイルはリポジトリ直下に作らず `%TEMP%` を使う。使い捨てのプログラムでも出力先は
  絶対パスで受け取り、そうでなければ止める（相対パスはリポジトリ直下に解決される）
- **複数ファイルへの一括置換をスクリプトで行ったら、`git diff` とビルドで健全性を確かめてから
  次へ進む。** 確認を検索結果だけで済ませない（置換が壊れると、検索語ごと化けて 0 件になる）。
  壊したら `git checkout HEAD -- <ファイル>` で戻して `git diff HEAD` が空になるまで確かめ、
  エディタで 1 箇所ずつやり直す
