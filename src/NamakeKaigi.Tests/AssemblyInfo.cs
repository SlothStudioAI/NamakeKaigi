using Xunit;

// 画面の試験は、全体で1つの Application とテーマ(色)を共有する。並行して走ると、ある試験がテーマを切り替えている途中に
// 別の試験が描いて色が混ざったり、窓の開け閉めが重なったりするので、試験は1つずつ順に走らせる。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
