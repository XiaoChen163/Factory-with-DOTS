# Scripts 领域目录整理验证

日期：2026-11-04。

## 改动范围

- 按技术层下的领域子目录移动 105 个 C# 文件；Scripts 下仍为 113 个 C# 文件。
- 原脚本 `.meta` 随文件移动；Unity 6000.3.19f1 导入生成 34 个新领域目录的 `.meta`。
- 不修改 C# 内容、类名、命名空间、程序集定义或玩法规则；不拆分历史混合组件文件。
- 更新文档中的具体源码路径，新增 [源码目录导航](../Guide/Scripts-Directory-Map.md)。

## 静态验证

- 对照 Git HEAD，113 个脚本及其原 `.meta` 内容一致（忽略 Git/Windows 的 CRLF/LF 差异）。
- 逐文件检查最近的 asmdef，113 个脚本的程序集归属均不变。
- Scripts 下无缺失 `.meta`、孤立 `.meta` 或重复 GUID。
- 已有脚本 GUID 保留；不通过删除脚本 `.meta` 重新分配身份。

## Unity 验证

- Unity 完成目录导入和脚本编译，未发现 C# 编译错误。
- 无图形模式第一次 EditMode：337 项中 331 项通过，6 项失败；其中 4 项与编辑器 UI/渲染环境相关，因此改用带图形批处理复测。
- 带图形 EditMode：337 项中 335 项通过，2 项失败：
  - `BuildingDefinitionTests.MainTable_ContainsOnlyUniversalFields`：测试固定预期 26 行，现有建筑表为 27 行。
  - `BuildingDefinitionTests.ProcessorMappings_AreReadFromExtension_AndCanShareMachineType`：测试构造的加工映射未包含现有 `testMachine`。
- 已核对 Git HEAD 中的原始测试与建筑表，上述两项不一致在目录整理前已存在；本次不修改测试或内容数据。
- PlayMode：36 项全部通过，包含场景、角色、碰撞和 UI 交互回归。

本地日志和 XML 位于 `Logs/scripts-layout-editmode*`、`Logs/scripts-layout-playmode*`；移动清单位于 `Logs/scripts-layout-moves.json`。Logs 不进入版本控制。
