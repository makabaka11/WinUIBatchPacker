# SevenZipExtractor 托管接口

此目录包含 [SevenZipExtractor 1.0.19](https://github.com/adoconnection/SevenZipExtractor) 的源码，按 [MIT 许可证](LICENSE)使用。项目直接传入应用内置的 `7z.dll` 路径调用 7-Zip 原生接口，不启动外部解压程序。

为适配当前 .NET 版本，移除了已过时的 CER 属性与二进制序列化构造函数。原生 DLL 的来源和许可证见相邻的 [7Zip 目录](../7Zip/README.md)。
