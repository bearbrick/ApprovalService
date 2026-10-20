using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Definitions;

/// <summary>
/// 只分析节点 ID 和有向连线的图结构，回路通过已访问集合终止搜索。
/// </summary>
internal sealed class FlowTopology
{
    /// <summary>
    /// 每个节点直接指向的后继节点。
    /// </summary>
    private readonly Dictionary<string, List<string>> outgoing;

    /// <summary>
    /// 每个节点的直接前驱节点。
    /// </summary>
    private readonly Dictionary<string, List<string>> incoming;

    /// <summary>
    /// 用已确认端点存在的连线建立双向邻接表。
    /// </summary>
    /// <param name="nodeIds">唯一且非空的节点 ID。</param>
    /// <param name="edges">端点已经确认存在的连线。</param>
    public FlowTopology(IEnumerable<string> nodeIds, IEnumerable<FlowEdge> edges)
    {
        outgoing = nodeIds.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        incoming = nodeIds.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            outgoing[edge.SourceId].Add(edge.TargetId);
            incoming[edge.TargetId].Add(edge.SourceId);
        }
    }

    /// <summary>
    /// 节点的有效出线数量。
    /// </summary>
    /// <param name="nodeId">节点 ID。</param>
    /// <returns>出线数量。</returns>
    public int OutDegree(string nodeId) => outgoing[nodeId].Count;

    /// <summary>
    /// 节点的有效入线数量。
    /// </summary>
    /// <param name="nodeId">节点 ID。</param>
    /// <returns>入线数量。</returns>
    public int InDegree(string nodeId) => incoming[nodeId].Count;

    /// <summary>
    /// 从指定节点向后查找所有可到达节点。
    /// </summary>
    /// <param name="nodeId">起点 ID。</param>
    /// <returns>包含起点的可达节点集合。</returns>
    public HashSet<string> ReachableFrom(string nodeId) => Traverse(nodeId, outgoing, null);

    /// <summary>
    /// 从指定节点向前查找所有能抵达它的节点。
    /// </summary>
    /// <param name="nodeId">终点 ID。</param>
    /// <returns>包含终点的可达节点集合。</returns>
    public HashSet<string> CanReach(string nodeId) => Traverse(nodeId, incoming, null);

    /// <summary>
    /// 检查是否有一条不经过指定节点的路径。
    /// </summary>
    /// <param name="startId">路径起点。</param>
    /// <param name="targetId">路径终点。</param>
    /// <param name="excludedId">不得经过的节点。</param>
    /// <returns>有这样的路径时为真。</returns>
    public bool HasPathAvoiding(string startId, string targetId, string excludedId) =>
        Traverse(startId, outgoing, excludedId).Contains(targetId);

    /// <summary>
    /// 遍历有向图，借已访问集合安全处理返工回路。
    /// </summary>
    /// <param name="startId">遍历起点。</param>
    /// <param name="adjacency">有向邻接表。</param>
    /// <param name="excludedId">不允许经过的节点；为空时不排除。</param>
    /// <returns>可到达节点集合。</returns>
    private static HashSet<string> Traverse(string startId, Dictionary<string, List<string>> adjacency, string? excludedId)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        if (startId == excludedId || !adjacency.ContainsKey(startId))
        {
            return visited;
        }

        var queue = new Queue<string>();
        queue.Enqueue(startId);
        visited.Add(startId);
        while (queue.Count > 0)
        {
            foreach (var next in adjacency[queue.Dequeue()])
            {
                if (next != excludedId && visited.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return visited;
    }
}
