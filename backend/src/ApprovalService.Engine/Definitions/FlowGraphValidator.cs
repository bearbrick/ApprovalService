using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Definitions;

/// <summary>
/// 发布前的画布结构检查；问题全部累积，避免设计人员逐次发布才发现下一处错误。
/// </summary>
internal static class FlowGraphValidator
{
    /// <summary>
    /// 校验节点、连线、可达性和成对网关。
    /// </summary>
    /// <param name="canvas">待发布画布。</param>
    /// <param name="issues">收集全部发布问题。</param>
    /// <returns>端点有效时构建的图结构，供后续条件检查使用。</returns>
    public static FlowTopology Validate(FlowCanvas canvas, List<FlowValidationIssue> issues)
    {
        var nodes = canvas.Nodes ?? [];
        var edges = canvas.Edges ?? [];
        var uniqueNodes = new Dictionary<string, FlowNode>(StringComparer.Ordinal);
        var edgeIds = new HashSet<string>(StringComparer.Ordinal);
        var validEdges = new List<FlowEdge>();

        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                Add(issues, null, "节点缺少 ID。");
            }
            else if (!uniqueNodes.TryAdd(node.Id, node))
            {
                Add(issues, node.Id, $"节点 ID“{node.Id}”重复。");
            }

            if (!IsKnownNodeType(node.Type))
            {
                Add(issues, node.Id, $"节点“{node.Id}”的类型不受支持。");
            }
        }

        foreach (var edge in edges)
        {
            if (string.IsNullOrWhiteSpace(edge.Id))
            {
                Add(issues, null, "连线缺少 ID。");
            }
            else if (!edgeIds.Add(edge.Id))
            {
                Add(issues, edge.Id, $"连线 ID“{edge.Id}”重复。");
            }

            if (!uniqueNodes.ContainsKey(edge.SourceId))
            {
                Add(issues, edge.Id, $"连线“{edge.Id}”的起点不存在。");
            }

            if (!uniqueNodes.ContainsKey(edge.TargetId))
            {
                Add(issues, edge.Id, $"连线“{edge.Id}”的终点不存在。");
            }

            if (edge.SourceId == edge.TargetId && uniqueNodes.ContainsKey(edge.SourceId))
            {
                Add(issues, edge.Id, $"连线“{edge.Id}”不能直接连回自身节点。");
            }

            if (uniqueNodes.ContainsKey(edge.SourceId) && uniqueNodes.ContainsKey(edge.TargetId))
            {
                validEdges.Add(edge);
            }
        }

        var topology = new FlowTopology(uniqueNodes.Keys, validEdges);
        ValidateStartAndEnd(uniqueNodes.Values, topology, issues);
        ValidateGateways(uniqueNodes.Values, validEdges, topology, issues);
        return topology;
    }

    /// <summary>
    /// 检查唯一的开始和结束，以及所有节点能进入、能离开流程。
    /// </summary>
    /// <param name="nodes">唯一 ID 的节点。</param>
    /// <param name="topology">画布连线结构。</param>
    /// <param name="issues">收集全部问题。</param>
    private static void ValidateStartAndEnd(IEnumerable<FlowNode> nodes, FlowTopology topology, List<FlowValidationIssue> issues)
    {
        var materialized = nodes.ToArray();
        var starts = materialized.Where(node => node.Type == FlowNodeTypes.Start).ToArray();
        var ends = materialized.Where(node => node.Type == FlowNodeTypes.End).ToArray();
        if (starts.Length != 1)
        {
            Add(issues, null, "流程必须有且只有一个开始节点。");
        }

        if (ends.Length != 1)
        {
            Add(issues, null, "流程必须有且只有一个结束节点。");
        }

        foreach (var start in starts)
        {
            if (topology.InDegree(start.Id) != 0 || topology.OutDegree(start.Id) != 1)
            {
                Add(issues, start.Id, $"开始节点“{start.Id}”必须没有入线且只有一条出线。");
            }
        }

        foreach (var end in ends)
        {
            if (topology.OutDegree(end.Id) != 0 || topology.InDegree(end.Id) == 0)
            {
                Add(issues, end.Id, $"结束节点“{end.Id}”不能有出线，且至少有一条入线。");
            }
        }

        foreach (var node in materialized.Where(node => node.Type is not (FlowNodeTypes.Start or FlowNodeTypes.End) && !IsGateway(node.Type)))
        {
            if (topology.InDegree(node.Id) == 0 || topology.OutDegree(node.Id) != 1)
            {
                Add(issues, node.Id, $"节点“{node.Id}”必须至少有一条入线且只有一条出线；分支请使用网关。");
            }
        }

        // 正反向各遍历一次即可同时发现悬空节点与无出口的回路，不会被回路卡住。
        var fromStart = starts.Length == 1 ? topology.ReachableFrom(starts[0].Id) : null;
        var toEnd = ends.Length == 1 ? topology.CanReach(ends[0].Id) : null;
        foreach (var node in materialized)
        {
            if (fromStart is not null && !fromStart.Contains(node.Id))
            {
                Add(issues, node.Id, $"节点“{node.Id}”无法从开始节点到达。");
            }

            if (toEnd is not null && !toEnd.Contains(node.Id))
            {
                Add(issues, node.Id, $"节点“{node.Id}”无法走到结束节点。");
            }
        }
    }

    /// <summary>
    /// 校验所有网关方向、度数、成对关系以及并行分支不能绕过汇合。
    /// </summary>
    /// <param name="nodes">唯一 ID 的节点。</param>
    /// <param name="edges">有效端点的连线。</param>
    /// <param name="topology">画布连线结构。</param>
    /// <param name="issues">收集全部问题。</param>
    private static void ValidateGateways(IEnumerable<FlowNode> nodes, List<FlowEdge> edges, FlowTopology topology, List<FlowValidationIssue> issues)
    {
        var materialized = nodes.ToArray();
        var gateways = materialized.Where(node => IsGateway(node.Type)).ToArray();
        foreach (var gateway in gateways)
        {
            if (gateway.GatewayDirection is not (FlowGatewayDirections.Split or FlowGatewayDirections.Join))
            {
                Add(issues, gateway.Id, $"网关“{gateway.Id}”必须指定分叉或汇合方向。");
            }

            // 排他分支可把一条线接回前面的节点（返工回路），这种分叉不需要汇合网关。
            if ((gateway.Type is FlowNodeTypes.ParallelGateway or FlowNodeTypes.InclusiveGateway) &&
                string.IsNullOrWhiteSpace(gateway.GatewayPairId))
            {
                Add(issues, gateway.Id, $"网关“{gateway.Id}”必须指定配对 ID。");
            }

            if (gateway.GatewayDirection == FlowGatewayDirections.Split &&
                (topology.InDegree(gateway.Id) != 1 || topology.OutDegree(gateway.Id) < 2))
            {
                Add(issues, gateway.Id, $"分叉网关“{gateway.Id}”必须有一条入线和至少两条出线。");
            }

            if (gateway.GatewayDirection == FlowGatewayDirections.Join &&
                (topology.InDegree(gateway.Id) < 2 || topology.OutDegree(gateway.Id) != 1))
            {
                Add(issues, gateway.Id, $"汇合网关“{gateway.Id}”必须有至少两条入线和一条出线。");
            }

            if (gateway.GatewayDirection == FlowGatewayDirections.Split &&
                gateway.Type is FlowNodeTypes.ExclusiveGateway or FlowNodeTypes.InclusiveGateway)
            {
                ValidateConditionalEdges(gateway, edges, issues);
            }
        }

        foreach (var group in gateways.Where(node => !string.IsNullOrWhiteSpace(node.GatewayPairId))
                     .GroupBy(node => node.GatewayPairId!, StringComparer.Ordinal))
        {
            var members = group.ToArray();
            var split = members.SingleOrDefault(node => node.GatewayDirection == FlowGatewayDirections.Split &&
                members.Count(candidate => candidate.GatewayDirection == FlowGatewayDirections.Split) == 1);
            var join = members.SingleOrDefault(node => node.GatewayDirection == FlowGatewayDirections.Join &&
                members.Count(candidate => candidate.GatewayDirection == FlowGatewayDirections.Join) == 1);
            if (members.Length != 2 || split is null || join is null || split.Type != join.Type)
            {
                foreach (var member in members)
                {
                    Add(issues, member.Id, $"网关配对“{group.Key}”必须恰好包含同类型的一处分叉和一处汇合。");
                }

                continue;
            }

            foreach (var edge in edges.Where(edge => edge.SourceId == split.Id))
            {
                if (!topology.ReachableFrom(edge.TargetId).Contains(join.Id))
                {
                    Add(issues, edge.Id, $"分支“{edge.Id}”无法到达配对汇合网关“{join.Id}”。");
                }
            }

            // 并行和包容分支会同时产生令牌；若某条路径绕过汇合直接结束，汇合便可能永远等不齐。
            if (split.Type is FlowNodeTypes.ParallelGateway or FlowNodeTypes.InclusiveGateway)
            {
                foreach (var end in materialized.Where(node => node.Type == FlowNodeTypes.End))
                {
                    foreach (var edge in edges.Where(edge => edge.SourceId == split.Id))
                    {
                        if (topology.HasPathAvoiding(edge.TargetId, end.Id, join.Id))
                        {
                            Add(issues, edge.Id, $"分支“{edge.Id}”可以绕过汇合网关“{join.Id}”到达结束节点。");
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 条件分叉必须有唯一兜底线，其余每条线必须带条件。
    /// </summary>
    /// <param name="gateway">条件分叉网关。</param>
    /// <param name="edges">有效连线。</param>
    /// <param name="issues">收集全部问题。</param>
    private static void ValidateConditionalEdges(FlowNode gateway, List<FlowEdge> edges, List<FlowValidationIssue> issues)
    {
        var outgoing = edges.Where(edge => edge.SourceId == gateway.Id).ToArray();
        if (outgoing.Count(edge => edge.IsDefault) != 1)
        {
            Add(issues, gateway.Id, $"条件网关“{gateway.Id}”必须恰好有一条兜底连线。");
        }

        foreach (var edge in outgoing)
        {
            if (!edge.IsDefault && string.IsNullOrWhiteSpace(edge.Condition))
            {
                Add(issues, edge.Id, $"条件连线“{edge.Id}”缺少条件。");
            }

            if (edge.IsDefault && !string.IsNullOrWhiteSpace(edge.Condition))
            {
                Add(issues, edge.Id, $"兜底连线“{edge.Id}”不能设置条件。");
            }
        }
    }

    /// <summary>
    /// 判断节点是否为已支持的节点种类。
    /// </summary>
    /// <param name="type">节点种类。</param>
    /// <returns>支持时为真。</returns>
    private static bool IsKnownNodeType(string type) => type is FlowNodeTypes.Start or FlowNodeTypes.End or
        FlowNodeTypes.Approval or FlowNodeTypes.Handling or FlowNodeTypes.Cc or FlowNodeTypes.ExclusiveGateway or
        FlowNodeTypes.InclusiveGateway or FlowNodeTypes.ParallelGateway or FlowNodeTypes.Subprocess or FlowNodeTypes.Automatic;

    /// <summary>
    /// 判断节点是否为网关。
    /// </summary>
    /// <param name="type">节点种类。</param>
    /// <returns>是网关时为真。</returns>
    private static bool IsGateway(string type) => type is FlowNodeTypes.ExclusiveGateway or FlowNodeTypes.InclusiveGateway or FlowNodeTypes.ParallelGateway;

    /// <summary>
    /// 加入一条中文发布问题。
    /// </summary>
    /// <param name="issues">问题集合。</param>
    /// <param name="elementId">相关画布元素。</param>
    /// <param name="message">问题说明。</param>
    private static void Add(List<FlowValidationIssue> issues, string? elementId, string message) =>
        issues.Add(new FlowValidationIssue { ElementId = elementId, Message = message });
}
