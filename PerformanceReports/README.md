# Performance Reports

性能测试、压力测试和最终验收报告统一存放在此目录，与 `Docs/` 中的开发指南
分开维护。

## 目录

- `4096-baseline/`：4096 节点优化前基线
- `phase1-*`、`phase2-*`、`phase3-*`、`phase4-*`：各阶段固定规模性能报告
- `phase5-20260807/`：Phase 5 建造/拆除尖峰优化、基线对照与最终验收
- `multilevel-stage0-20260814/`：多层稀疏网格改造阶段 0 行为与性能基线
- `StressTest/`：按场景和运行时间归档的压力测试结果

## 常用入口

- Phase 5 最终验收：
  [`phase5-20260807/README.md`](phase5-20260807/README.md)
- 压力测试汇总：
  [`StressTest/README.md`](StressTest/README.md)
