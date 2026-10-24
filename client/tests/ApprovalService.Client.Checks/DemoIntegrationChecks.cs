using ApprovalService.Client;
using ApprovalService.Demo.Integration;

namespace ApprovalService.Client.Checks;

/// <summary>
/// 示例业务系统的原单、审批快照和回调重发检查。
/// </summary>
internal static class DemoIntegrationChecks
{
    /// <summary>
    /// 验证业务文件独立持久化及回调事件去重。
    /// </summary>
    /// <param name="check">检查报告。</param>
    internal static void Run(Action<string, bool> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "approval-demo-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new DemoDocumentStore(path);
            store.SaveExpense(new ExpenseClaim
            {
                BusinessKey = "EXP-1", Initiator = "staff-1", Department = "dept-1",
                Title = "差旅报销", Category = "交通", ClaimAmount = 860m,
                ReceiptFileId = "file-1", Lines = new List<ExpenseLine>
                {
                    new() { Date = "2026-10-24", Description = "火车票", Amount = 860m },
                },
            });
            store.SavePurchase(new PurchaseRequest
            {
                BusinessKey = "PUR-1", Initiator = "staff-2", Department = "dept-2",
                Title = "采购物料", BaseAmount = 100m, TaxAmount = 13m,
                Items = new List<PurchaseLine> { new() { ItemName = "电源", Quantity = 2, UnitPrice = 50m } },
            });
            var expense = store.FindExpense("EXP-1") ?? throw new Exception("报销单保存后消失。");
            expense.Title = "未保存的修改";
            check("业务原单读取为副本且不会绕过落盘", store.FindExpense("EXP-1")?.Title == "差旅报销");
            check("费用报销快照按流程字段生成明细与票据", expense.ToApprovalData()["receipt"] as string == "file-1"
                && expense.ToApprovalData()["lines"] is List<Dictionary<string, object?>> lines && lines.Count == 1);

            store.RecordStart(new ApprovalInstance
            {
                Id = "approval-exp-1", FlowCode = "EXPENSE", BusinessKey = "EXP-1", Status = "running",
            });
            var notification = new ApprovalCallback
            {
                EventId = "event-1", InstanceId = "approval-exp-1", FlowCode = "EXPENSE", BusinessKey = "EXP-1",
                Status = "approved", FieldValues = new Dictionary<string, object?>
                {
                    ["verifiedAmount"] = 800m,
                    ["paymentReference"] = "PAY-1",
                },
            };
            check("首次回调把核定金额和付款流水独立写回原单", store.ApplyCallback(notification)
                && store.FindExpense("EXP-1") is { VerifiedAmount: 800m, PaymentReference: "PAY-1", ApprovalStatus: "approved" });
            notification.Status = "rejected";
            check("相同事件 ID 重发不覆盖已同步业务状态", !store.ApplyCallback(notification)
                && store.FindExpense("EXP-1")?.ApprovalStatus == "approved");

            var reopened = new DemoDocumentStore(path);
            check("重启后保留业务原单和已处理事件游标", reopened.FindExpense("EXP-1")?.VerifiedAmount == 800m
                && reopened.FindPurchase("PUR-1")?.BaseAmount == 100m && !reopened.ApplyCallback(notification));

            reopened.UpdateFromQuery(new ApprovalInstance
            {
                Id = "approval-pur-1", FlowCode = "PURCHASE_REQUEST", BusinessKey = "PUR-1", Status = "running",
                ApproverFieldValues = new Dictionary<string, object?> { ["approvedBudget"] = 110m },
            });
            check("无业务后端时查询同步采购审批人字段", reopened.FindPurchase("PUR-1") is
                { ApprovalStatus: "running", ApprovedBudget: 110m, ApprovalInstanceId: "approval-pur-1" });
            var orderCallback = new ApprovalCallback
            {
                EventId = "event-order", InstanceId = "approval-pur-1", FlowCode = "PURCHASE_REQUEST",
                BusinessKey = "PUR-1", Status = "approved", ActionResults = new Dictionary<string, object?>
                {
                    ["action-1"] = new Dictionary<string, object?> { ["orderNo"] = "PO-1" },
                },
            };
            check("自动动作结果独立回写采购订单号", reopened.ApplyCallback(orderCallback)
                && reopened.FindPurchase("PUR-1")?.OrderNumber == "PO-1");
            check("不同审批单不能覆盖已关联业务原单", Throws<InvalidOperationException>(() => reopened.ApplyCallback(
                new ApprovalCallback { EventId = "wrong", InstanceId = "other", FlowCode = "PURCHASE_REQUEST",
                    BusinessKey = "PUR-1", Status = "approved" })));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>
    /// 判断可纠正冲突是否按预期被拒绝。
    /// </summary>
    /// <typeparam name="TException">预期异常。</typeparam>
    /// <param name="action">待执行操作。</param>
    /// <returns>只在抛出预期类型时为 true。</returns>
    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }
}
