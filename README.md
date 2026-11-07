# iKVM Connector

解决 **ATEN / Supermicro BMC 的 Java iKVM（JNLP）在证书过期后无法连接** 的小工具。

只做一件事：在你点“连接”时，自动登录 BMC、取一张新的 JNLP、把 BMC 时钟拨回一年，
然后用本机 Java 8 启动官方的 iKVM Viewer。附带的“服务器信息”窗口可以把 BMC 的
硬件、网络、审计、功耗、传感器信息集中看一眼。

> 仓库里**不含**任何 ATEN 的 jar、DLL 或客户端证书（版权归厂商），
> 需要你自己从 JNLP / 本机 JWS 缓存里取出（见“准备 viewer 目录”）。

## 适用型号

下面的结论来自这台机器的实测，型号 / 版本都记在这里：

| 项目 | 型号 / 版本 |
| --- | --- |
| 整机 | Supermicro **SYS-7048GR-TR** |
| 主板 | Supermicro **X10DRG-Q** |
| 机箱 | Supermicro **CSE-747BTS-R2K04BP** |
| iKVM | ATEN Java iKVM Viewer **V1.69.38**（主 jar `iKVM__V1.69.38.0x0.jar`，主类 `tw.com.aten.ikvm.KVMMain`） |
| BMC 固件 | `BL_SUPERMICRO_X7SB3_2020-07-17_B0070`（IPMIFW 0390，构建 07/17/2020） |
| BMC Web | 1.1（内核 2.6.28.9） |
| BIOS | 3.2（11/22/2019） |
| 出问题的证书 | iKVM jar 内置的**客户端**证书，**2026-05-17 到期**（`res/client.crt` / `res/client.key`） |

凡是同一套 ATEN iKVM 的机器（X9 / X10 / X11 代 Supermicro 上很常见：JNLP 主类是
`tw.com.aten.ikvm.KVMMain`、走双向 TLS、jar 里带 `client.crt`/`client.key`），
失效原因和这里的处理方式都一样；其它厂商的 iKVM 只要也是“客户端证书过期 + BMC 时钟超前”，
同样可以参考。

---

## 现象

双击 BMC 网页导出的 `launch.jnlp`，Java 窗口一闪而过，或者弹出：

```
Connection failed
Retry =1
...
```

命令行里能看到类似 `javax.net.ssl.SSLHandshakeException: Received fatal alert: certificate_expired`。

## 原因

不是“服务端证书过期”这么简单，实际链路是：

1. iKVM 用的是**双向 TLS**：jar 里内置了客户端证书（`res/client.crt` / `res/client.key`），
   服务端（BMC）要求出示它。
2. 那张内置的**客户端证书在 2026-05-17 到期**（SYS-7048GR-TR / X10DRG-Q 这台机器上就是这一张，
   2020-07-17 那版 BMC 固件烧进去的），而 BMC 的时钟已经走到 2026 年以后
   （BMC 会跟随主机时间，NTP 关掉也没用，它会自己同步回去）。
3. 于是 BMC 在校验客户端证书时判定“证书已过期”，直接回 `alert 45 (certificate_expired)`，
   握手失败。

服务端那张自签证书本身是十年有效期的，跟这个报错没关系；换掉服务端证书也没用——
`client.crt` 是烧在 jar 里的，网页端也换不了。

**结论：把 BMC 的时钟拨回证书有效期之内，握手立刻恢复正常。**
代价只是 BMC 自己的日志时间戳会跟着变，服务器本身不受影响。

## 做法

程序做的事（`MainForm.ConnectWork`）：

1. `POST /cgi/login.cgi` 登录 BMC，拿 `SID` Cookie；
2. 需要时 `POST /cgi/op.cgi` 设时间（年份 = 你选的年份，月日时分秒沿用当前 BMC 时间；
   若算出来仍越过 2026-05-17，就退到 2026-05-01 12:00）；
3. `GET /cgi/url_redirect.cgi?url_name=ikvm&url_type=jwsk` 取一张**新令牌**的 JNLP
   （网页导出的那份 JNLP 里的令牌几分钟就失效，这就是“Retry =1..10”的另一个来源）；
4. 用 `java -cp iKVM.jar;nativelib.jar tw.com.aten.ikvm.KVMMain <11 个参数>` 启动 Viewer。

两个踩过的坑，代码里都做了处理：

* JNLP 的第 9 个参数（`stunEnable`）**必须是 1**。若为 0，`KVMMain.main` 会直接
  `return`，进程秒退、无任何输出——看起来像“Java 崩了”，其实是它什么都不做。
* Viewer 自己会解压 jar 里的 `stunnel.exe` 到 `%TEMP%\<pid>\`，并在 `127.0.0.1:63630/63631`
  起两个本地隧道。上一次异常退出留下的 stunnel 进程会占住端口，导致一直 Retry，
  所以启动前会清理残留进程和临时目录。

## 环境要求

* Windows 7 及以上，.NET Framework 4.x（Win10/11 自带）
* **Java 8**（jre1.8）。程序会依次查找：
  * `%JAVA_HOME%\bin\java.exe`
  * `C:\Program Files (x86)\Common Files\Oracle\Java\java8path\java.exe`
* 能访问 BMC 的 443 端口

## 准备 viewer 目录

编译出的 exe 旁边需要这样一份 `viewer\`：

```
iKVMConnector.exe
viewer\
  iKVM.jar          # 来自 JNLP 的 main jar（iKVM__V1.69.38.0x0.jar）
  nativelib.jar     # 来自 JNLP 的 nativelib
  native\
    iKVM64.dll
    SharedLibrary64.dll
```

取法：

* 让浏览器/Java Web Start 先跑一次 JNLP（哪怕最后握手失败），它会把 jar 落到
  `%LOCALAPPDATA%Low\Sun\Java\Deployment\cache\6.0\...`；
* 或者直接按 JNLP 里的 `codebase` + jar 名从 BMC 下载（部分固件会 404，那就只能用缓存）；
* 两个 jar 里是 pack200 压缩的签名包，用 `jar xf` 或直接 `java -cp` 都能用，
  不需要签名校验（我们不是通过 JNLP 启动，不走 SecurityManager）。

## 编译

只依赖 .NET Framework 自带的 csc，不需要 Visual Studio：

```bat
build.cmd
```

或手动：

```bat
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
%CSC% -nologo -codepage:65001 -target:winexe -out:dist\iKVMConnector.exe ^
  -r:System.dll -r:System.Security.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll ^
  src\Program.cs src\MainForm.cs src\Core.cs src\Bmc.cs src\InfoForm.cs

%CSC% -nologo -codepage:65001 -target:exe -out:dist\iKVM-diag.exe ^
  -r:System.dll src\Core.cs src\Diag.cs src\DiagMain.cs
```

## 使用

1. 双击 `iKVMConnector.exe`（或直接拖一个 `.jnlp` 进去）。
2. 填 BMC 地址、用户名、密码，勾上“连接前把时钟拨回”，年份选去年（默认就是去年）。
3. 点“连接”。日志会依次走：登录 BMC → 拨时钟 → 取新令牌 → 启动 Java Viewer，
   约 8 秒后出现 `Java iKVM Viewer v1.69.38 [...]` 窗口即成功。
4. 地址、用户、密码、勾选状态、年份都会记住；密码用 Windows DPAPI 按当前用户加密，
   存放在 `%APPDATA%\iKVMConnector\settings.ini`（写不进去时退回程序目录）。

命令行：

```
iKVMConnector.exe                     # 打开界面
iKVMConnector.exe --auto              # 打开后立刻按上次的设置连接
iKVMConnector.exe --info              # 打开后直接弹“服务器信息”
iKVMConnector.exe --host=1.2.3.4 --user=u --pass=p
iKVMConnector.exe D:\path\launch.jnlp
```

## 服务器信息窗口

点“服务器信息”另开一个只读窗口，读取的都是 BMC 自己的 CGI 接口
（需要先登录，接口都要带 `CSRF_TOKEN` 头；POST 必须禁掉 `Expect: 100-continue`，否则 BMC 返回 417）：

| 区块 | 来源 | 内容 |
| --- | --- | --- |
| 基本信息 | `GENERIC_INFO.XML` / `Get_PlatformInfo.XML` | BMC MAC、IPMI 固件版本与构建日期、Web 版本、主板双 MAC、BIOS、CPLD、Redfish、BMC 时间、开关机状态、SEL 条数 |
| 硬件信息 | `FRU_INFO.XML` | 主板/机箱/整机型号与序列号、生产日期 |
| 网络 | `CONFIG_INFO.XML` | IP/掩码/网关、BMC 与网关 MAC、DHCP、VLAN、RMCP 端口、BMC 用户与权限 |
| 最近操作 | `GET_SYS_ELOG.XML` | 审计日志最近 10 条（时间 / 用户 / IP） |
| 功耗 | `POWER_CONSUMPTION.XML` | 当前/峰值/最低、峰值时刻、近 12 小时、今日统计 |
| 传感器 | `SENSOR_INFO.XML` | 全部温度/风扇/电压读数与阈值状态、风扇模式 |

传感器阈值的注意点：BMC 给的 `UNR/UC/UNC/LNC/LC/LNR` 是 **8 位原始读数**，不是工程单位；
其中温度按有符号字节解释（会出现 `LNR = 0xf6` 即 −10 ℃），电压/风扇按无符号解释
（风扇的上限 `0xfd` 实际等于“没有上限”）。拿换算后的数值去比阈值会全线误报。

每次读取还会把同样的内容另存为程序目录下的 `服务器信息.txt`。

## 目录结构

```
src/Program.cs     入口与命令行参数
src/MainForm.cs    主窗口：设置持久化、连接流程、进程管理
src/Bmc.cs         BMC 网页接口客户端（登录、CSRF、时间、JNLP、IPMI XML）
src/InfoForm.cs    服务器信息窗口与各个 XML 的解析
src/Core.cs        JNLP 解析、TLS 隧道、错误信息解释
src/Diag.cs        握手诊断
src/StunnelMain.cs 用 .NET 重写 stunnel 的尝试（保留，未使用）
src/TunnelMain.cs  命令行隧道
```

## 已知限制

* 只解决“客户端证书过期”这一种失败；BMC 固件换过、证书被替换时可能不适用。
* 拨时钟会影响 BMC 的 SEL / 审计日志时间戳（本工具把年份往前拨，日期时间不变）。
* 依赖 Java 8；Java 11+ 里 iKVM 的老 JNI 库和 `sun.java2d.opengl` 不一定能用。
* `StunnelMain.cs`（用 .NET 复刻 stunnel）在本机实测拿不到 PFX 私钥，
  最终还是用 jar 里自带的 `stunnel.exe`，这段代码仅作参考。

## 免责声明

仅用于你**自有或被授权管理**的服务器。修改 BMC 时钟、使用厂商的私有客户端属于
你自己的运维决定，风险自负。仓库不包含任何厂商二进制或证书。
