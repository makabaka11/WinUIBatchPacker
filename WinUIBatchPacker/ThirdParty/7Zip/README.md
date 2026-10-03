# 7-Zip 组件

`7z.dll` 来自 [7-Zip 26.03 x64 官方发行包](https://github.com/ip7z/7zip/releases/tag/26.03) `7z2603-x64.exe`，该发行包 SHA-256 为 `0859c524b8a63551848f0c246abddcb1d0b7b656b0fbfe879f8d85e61a9e6edd`。应用把 DLL 作为资源打包，处理字体压缩包时释放到批次临时目录，随后通过 C# 接口直接调用。许可证见 [License.txt](License.txt)；7-Zip 源码见 [7-zip.org](https://www.7-zip.org/)。

托管接口来自 MIT 许可的 [SevenZipExtractor 1.0.19](https://github.com/adoconnection/SevenZipExtractor)。已移除其自带旧版 DLL 和过时的 .NET API 标记，源码与许可证位于相邻的 `SevenZipExtractor` 目录。
