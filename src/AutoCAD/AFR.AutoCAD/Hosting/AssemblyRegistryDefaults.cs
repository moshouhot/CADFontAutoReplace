using AFR.Hosting;
using AFR.Services;

// 声明插件 DLL 部署时可写入的外部 CAD 默认偏好项。
// 绿色版字体配置保存在 DLL 同目录 AFR.config.json；Applications\<AppName>
// 注册表键仅用于 AutoCAD 自动加载协议，不再保存 MainFont/BigFont/TrueTypeFont。
//
// 写入语义：
// - RegistryDefaultDwordAt：写到 <ProfileSubKey>\<SubPath> 下（典型如 FixedProfile\
//   General Configuration 等 CAD 自身偏好键）。
//     * ForceOverwrite=false（默认）：仅在值缺失时写入，等同上面两个特性的语义。
//     * ForceOverwrite=true：值缺失或现值不等于期望值时覆写；现值已等于期望值时
//       视为"用户预设"放行，不动数据也不打标记。
//     * RemoveOnUninstall=true：实际写入时在 Applications\<AppName>\__Owned\<SubPath>
//       下记录所有权标记；卸载时仅当外部键现值仍等于标记记录的值才删除，
//       从而避免误删用户预设以及安装后中途的手动修改。
//
// AutoCAD 协议键（LOADER / LOADCTRLS / MANAGED / DESCRIPTION）以及插件版本类标识
// （PluginVersion / PluginBuildId）由部署工具自身管理，不在此处声明。

// SHX 缺失对话框抑制不在注册表层做：经实测 AutoCAD 并未把"缺少 SHX 文件"对话框的
// 持久化状态写到注册表（包括 FixedProfile\General Configuration\FileDialog 这类候选）。
// 真实控制点是 %APPDATA%\Autodesk\AutoCAD <year>\R*\<lang>\Support\Profiles\FixedProfile.aws
// 中的 HideableDialog 节点，已迁至 AwsHideableDialogPatcher（共享层）通过 Apply/Cleanup 管理。
