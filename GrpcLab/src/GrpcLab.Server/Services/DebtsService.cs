using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using GrpcLab.Contracts;

namespace GrpcLab.Server.Services;

/// <summary>
/// Реализация сгенерированной базы Debts.DebtsBase. Данные фейковые —
/// суть проекта в транспорте, не в хранилище.
/// </summary>
public sealed class DebtsService : Debts.DebtsBase
{
    public override Task<DebtReply> GetDebt(GetDebtRequest request, ServerCallContext context)
    {
        if (request.DebtId <= 0)
        {
            // Ошибки в gRPC — не HTTP-статусы, а свои коды + RpcException.
            // InvalidArgument ≈ 400, NotFound ≈ 404, Unavailable ≈ 503 (ретраибельный)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "debt_id должен быть > 0"));
        }

        return Task.FromResult(new DebtReply
        {
            DebtId = request.DebtId,
            DebtorName = $"Должник №{request.DebtId}",
            PrincipalMinorUnits = 150_000_00, // 150 000.00 в копейках
            Currency = "RUB",
            Status = DebtStatus.Active,
        });
    }

    public override async Task StreamPayments(
        StreamPaymentsRequest request,
        IServerStreamWriter<PaymentReply> responseStream,
        ServerCallContext context)
    {
        for (var i = 1; i <= request.Count; i++)
        {
            // context.CancellationToken отменяется и при разрыве соединения,
            // и при истечении deadline клиента — сервер не молотит впустую.
            // Это главный ответ на «как работает deadline»: он ЕДЕТ С ЗАПРОСОМ
            // (заголовок grpc-timeout) и отменяет работу на обеих сторонах.
            await Task.Delay(100, context.CancellationToken);

            await responseStream.WriteAsync(new PaymentReply
            {
                AmountMinorUnits = i * 1000_00,
                PaidAt = Timestamp.FromDateTime(DateTime.UtcNow),
            }, context.CancellationToken);
        }
    }
}
