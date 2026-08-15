# 第三者ライセンス本文の出所

`THIRD-PARTY-NOTICES.txt` に収録した各ライセンス本文を、**どこから取得したか**の記録。
配布物には含めない開発者向けの文書で、**依存を上げたときに何を見直せばよいか**を
分かるようにするために置いている。

取得日はすべて **2026-08-15**。SHA-256 は先頭 16 桁。

## 何が入っているかの調べ方

同梱するバイナリは 2 本だけで、内訳は次の手順で調べた。同じ手順を繰り返せば、
**版が上がったときに何が変わったかを差分で確認できる。**

1. `OpenCvSharpExtern.dll` から印字可能 ASCII の連なりを抽出する
2. その中から `General configuration for OpenCV` を探す。OpenCV の
   `getBuildInformation()` 相当が埋め込まれており、**Media I/O・3rdparty
   dependencies・Extra dependencies に版つきで一覧が出る**
3. ただし**ビルド情報に出ないものがある。** Tesseract・Leptonica・libarchive・
   bzip2・zstd・libcurl・OpenSSL はここに出ない。vcpkg のビルドパス
   （`C:\vcpkg\buildtrees\<port>\src\<version>`）や、各ライブラリ自身の
   バージョン文字列・エラー文字列から特定した
4. **エラー文字列だけでは判断しない。** libarchive は圧縮バックエンドを
   リンクしていなくてもエラー文言を持つ。各ライブラリ**自身**の実装由来の
   文字列（例: zstd なら `ZSTD_getErrorString` の表、bzip2 なら
   `This is a bug in bzip2/libbzip2`）でリンクを確認した

## 取得元の一覧

すべて git のタグまたはコミットでピン留めした版から取得している。
**最新版の本文ではなく、実際にリンクされている版の本文**であること。

### 版が確定しているもの

| プロジェクト | 版 | 収集ファイル | バイト | SHA-256 | 取得元 |
|---|---|---|---:|---|---|
| OpenCV | 4.13.0 | `opencv-4.13.0.txt` | 11,358 | `CFC7749B96F63BD3` | `https://raw.githubusercontent.com/opencv/opencv/4.13.0/LICENSE` |
| OpenCvSharp | 4.13.0.20260627 | `opencvsharp-b161e7e.txt` | 11,336 | `93A73400A67BA640` | `https://raw.githubusercontent.com/shimat/opencvsharp/b161e7e012f5101f6d5dc68a835c59db6cc88b18/LICENSE` |
| Tesseract | 5.5.2 | `tesseract-5.5.2.txt` | 11,358 | `CFC7749B96F63BD3` | `https://raw.githubusercontent.com/tesseract-ocr/tesseract/5.5.2/LICENSE` |
| FlatBuffers | 25.9.23 | `flatbuffers-25.9.23.txt` | 11,358 | `CFC7749B96F63BD3` | `https://raw.githubusercontent.com/google/flatbuffers/v25.9.23/LICENSE` |
| CommunityToolkit.Mvvm | 8.4.2 | `communitytoolkit-mvvm-8.4.2-LICENSE.txt` | 1,158 | `651997EF19DBB9EC` | NuGet パッケージ同梱の `License.md` |
| 同上（第三者表記） | 8.4.2 | `communitytoolkit-mvvm-8.4.2-THIRDPARTY.txt` | 8,600 | `7774F8B0AB66BFB4` | NuGet パッケージ同梱の `ThirdPartyNotices.txt` |
| zlib | 1.3.1 | `zlib-1.3.1.txt` | 1,002 | `845EFC77857D485D` | `https://raw.githubusercontent.com/madler/zlib/v1.3.1/LICENSE` |
| libjpeg-turbo | 3.1.3 | `libjpeg-turbo-3.1.3.txt` | 5,620 | `2189DC45A8FE9620` | `https://raw.githubusercontent.com/libjpeg-turbo/libjpeg-turbo/3.1.3/LICENSE.md` |
| libjpeg-turbo（IJG 本文） | 3.1.3 | `libjpeg-turbo-3.1.3-README.ijg.txt` | 12,799 | `75815E3BF6484201` | `https://raw.githubusercontent.com/libjpeg-turbo/libjpeg-turbo/3.1.3/README.ijg` |
| libpng | 1.6.55 | `libpng-1.6.55.txt` | 5,345 | `BDB0A645EA18C605` | `https://raw.githubusercontent.com/pnggroup/libpng/v1.6.55/LICENSE` |
| libwebp | 1.6.0 | `libwebp-1.6.0.txt` | 1,496 | `5AEC868F669E384A` | `https://raw.githubusercontent.com/webmproject/libwebp/v1.6.0/COPYING` |
| libtiff | 4.7.1 | `libtiff-4.7.1.txt` | 2,416 | `0E27C2382D7B8147` | `https://gitlab.com/libtiff/libtiff/-/raw/v4.7.1/LICENSE.md` |
| OpenJPEG | 2.5.3 | `openjpeg-2.5.3.txt` | 2,112 | `A6AF136F3E15038A` | `https://raw.githubusercontent.com/uclouvain/openjpeg/v2.5.3/LICENSE` |
| OpenEXR | 2.3.0 | `openexr-2.3.0.txt` | 1,700 | `2B2B83380132B61C` | `https://raw.githubusercontent.com/AcademySoftwareFoundation/openexr/v2.3.0/LICENSE` |
| Protocol Buffers | 3.19.1 | `protobuf-3.19.1.txt` | 1,732 | `6E5E117324AFD944` | `https://raw.githubusercontent.com/protocolbuffers/protobuf/v3.19.1/LICENSE` |
| libcurl | 8.18.0 | `curl-8.18.0.txt` | 1,088 | `E18F1989333B7004` | `https://raw.githubusercontent.com/curl/curl/curl-8_18_0/COPYING` |
| Intel IPPICV / IPP IW | 2026.0.0 | `ippicv-2026.0.0-EULA.txt` | 4,205 | `A786965C9053C8D0` | 下記の「IPPICV の取得手順」 |
| AMD OpenCL カーネル | OpenCV 4.13.0 同梱 | `amd-opencl-kernels-opencv-dnn.txt` | 1,411 | `528B7AFADF088390` | `https://raw.githubusercontent.com/opencv/opencv/4.13.0/modules/dnn/src/opencl/lrn.cl` のヘッダ |

### 版が特定できていないもの

`OpenCvSharpExtern.dll` にリンクされていることは確認済みだが、**版を示す文字列が
埋まっていない。** 現時点で最新の本文を収録している。

| プロジェクト | 収集ファイル | バイト | SHA-256 | 取得元 |
|---|---|---:|---|---|
| Leptonica | `leptonica.txt` | 1,521 | `87829ABB5BBB00B5` | `https://raw.githubusercontent.com/DanBloomberg/leptonica/master/leptonica-license.txt` |
| libarchive | `libarchive.txt` | 3,089 | `30E556B3959E3985` | `https://raw.githubusercontent.com/libarchive/libarchive/master/COPYING` |
| bzip2 | `bzip2.txt` | 1,895 | `452871C08826FFB3` | `https://gitlab.com/bzip2/bzip2/-/raw/master/COPYING` |
| XZ Utils (liblzma) | `xz-liblzma.txt` | 3,119 | `616A3AD264CE29B8` | `https://raw.githubusercontent.com/tukaani-project/xz/master/COPYING` |
| 同上（0BSD 本文） | `xz-liblzma-COPYING.0BSD.txt` | 607 | `0B01625D853911CD` | `https://raw.githubusercontent.com/tukaani-project/xz/master/COPYING.0BSD` |
| Zstandard | `zstd.txt` | 1,549 | `7055266497633C90` | `https://raw.githubusercontent.com/facebook/zstd/dev/LICENSE` |
| Intel ITT API (ittnotify) | `ittnotify-ittapi.txt` | 1,485 | `CF48CDD9ED87C203` | `https://raw.githubusercontent.com/intel/ittapi/master/LICENSES/BSD-3-Clause.txt` |
| OpenSSL | `openssl-3.5.txt` | 10,175 | `7D5450CB2D142651` | `https://raw.githubusercontent.com/openssl/openssl/openssl-3.5.0/LICENSE.txt` |

**版が変わってライセンスが変わりうるのは XZ Utils だけ。** 5.6.0（2024 年）で
パブリックドメインから 0BSD へ変わっている。ビルド日（2026-06-27）からは 5.6 以降と
みるのが自然だが確証はない。**どちらでも安全側は同じ**で、5.6 未満ならパブリック
ドメインなので表示義務が無く、0BSD の本文を載せることは過剰であっても不足しない。

OpenSSL は `ML-KEM-512` / `ML-DSA-44` などの実装が入っていることから **3.5 以降**と
特定した。パッチ版は不明だが、**3.x 系である限りライセンスは Apache-2.0 で変わらない。**

残り（Leptonica・libarchive・bzip2・Zstandard・ittnotify）は、**いずれも歴史的に
ライセンスが変わっていない**ため、最新の本文で足りると判断した。

## 個別の注意

- **libjpeg-turbo は `LICENSE.md` だけでは条文が完結しない。** IJG License の本文は
  `README.ijg` にあり、`LICENSE.md` はそこを参照しているだけ。**両方を収録すること**
- **XZ Utils の `COPYING` は要約で、条文は別ファイル。** リンクしているのは
  liblzma のみで、`COPYING` に「liblzma is under the BSD Zero Clause License (0BSD)」と
  明記されている。GPL / LGPL が関わるのはコマンドラインツールとビルド系で、
  **同梱物には含まれない**
- **ittnotify は OpenCV 同梱コピーにライセンスファイルが無い。** `3rdparty/ittnotify/` を
  実際に見に行ったが `CMakeLists.txt` と `include/` `src/` だけだった。やむを得ず
  上流 `intel/ittapi` の本文を採っている。**OpenCV 同梱コピーの表記そのものではない**
- **AMD の表記は DLL に逐語で埋め込まれている。** 抽出したものと
  `modules/dnn/src/opencl/lrn.cl` のヘッダが**完全一致することを確認済み**。
  出所としてはリポジトリ側を正とする
- **Apache License 2.0 のみ本文を共有できる。** OpenCV / Tesseract / FlatBuffers は
  バイト単位で完全一致（SHA-256 が同一）。OpenCvSharp との差は付録の記入例が
  `Copyright 2008 shimat` で埋まっている 3 行、OpenSSL との差は付録の省略と
  `http` / `https` の 27 行だけで、**いずれも許諾条件ではない**
- **BSD 3-Clause は共有できない。** 第 3 条に団体名が埋め込まれており、Google /
  Industrial Light &amp; Magic / Meta / Intel でそれぞれ本文が違う

## FFmpeg について

`opencv_videoio_ffmpeg4130_64.dll` は **LGPL v2.1** だが、**配布物から外すので
表記の対象外**。理由は [decisions.md](decisions.md) の「配布物に含めないもの」。

## IPPICV の取得手順

zip の中にしか無いので手順を残す。

1. `https://raw.githubusercontent.com/opencv/opencv/4.x/3rdparty/ippicv/ippicv.cmake` から
   `IPPICV_COMMIT` と Windows x64 の `OPENCV_ICV_NAME` を読む
   （2026-08-15 時点で `8338862a733cb3980d8b51d8e14917fe0e695f71` と
   `ippicv_2026.0.0_win_intel64_20260630_general.zip`）
2. `https://raw.githubusercontent.com/opencv/opencv_3rdparty/<commit>/ippicv/<name>` を取得（25.2MB）
3. 書庫内の `ippicv_win/EULA.rtf` を取り出し、テキストへ変換する
4. `ippicv_win/third-party-programs.txt` も確認する。2026.0.0 では
   「第三者プログラムは含まれない」と明言しており、**この EULA 1 本で閉じる**

## 依存を上げたときの手順

1. 「何が入っているかの調べ方」をやり直し、**版の一覧を作る**
2. 前回の一覧（この文書）と突き合わせ、**変わったものだけ**取得し直す
3. 新しく増えたライブラリが無いかを確認する。とくに**ビルド情報に出ない層**
   （Tesseract が引き込む libcurl / OpenSSL / libarchive など）に注意する
4. `THIRD-PARTY-NOTICES.txt` を作り直し、この文書の表を更新する

## 決めてあること

- `THIRD-PARTY-NOTICES.txt` は**リポジトリのルートに置き、zip にも同梱する。**
  利用者が受け取る配布物の一部であり、表示義務を満たすのは配布物側であるため
- **著作権表示は `Copyright (c) 2026 naganakidori` に揃える。** `LICENSE` は対応済み。
  `src/Soroe/Soroe.csproj` の `Copyright` は現在**空白 1 文字**なので、アセンブリの
  メタ情報を入れるときに同じ文字列にすること（`Version` `Description` と同時に行う）
