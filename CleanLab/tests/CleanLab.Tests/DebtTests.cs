using CleanLab.Application;
using CleanLab.Domain;
using CleanLab.Infrastructure;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace CleanLab.Tests;

// Выигрыш архитектуры в тестах: домен тестируется БЕЗ инфраструктуры вообще,
// application — с in-memory реализацией портов. Ни моков БД, ни WebApplicationFactory.

public class DomainTests
{
    [Fact]
    public void Money_DifferentCurrencies_CannotBeAdded() =>
        Assert.Throws<DomainException>(() => _ = Money.Rub(10) + new Money(10, "USD"));

    [Fact]
    public void Debt_FullPayment_ClosesAndRaisesEvent()
    {
        var debt = Debt.Open("К-1", Money.Rub(1000));
        debt.DequeueEvents(); // сбрасываем DebtOpened

        debt.RegisterPayment(Money.Rub(400));
        debt.RegisterPayment(Money.Rub(600));

        Assert.True(debt.IsClosed);
        Assert.Contains(debt.DequeueEvents(), e => e is DebtClosed);
    }

    [Fact]
    public void Debt_Overpayment_Throws()
    {
        var debt = Debt.Open("К-1", Money.Rub(1000));
        Assert.Throws<DomainException>(() => debt.RegisterPayment(Money.Rub(1500)));
    }

    [Fact]
    public void Debt_PaymentAfterClose_Throws()
    {
        var debt = Debt.Open("К-1", Money.Rub(100));
        debt.RegisterPayment(Money.Rub(100));
        Assert.Throws<DomainException>(() => debt.RegisterPayment(Money.Rub(1)));
    }
}

public class ApplicationTests
{
    private static IMediator BuildMediator()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<OpenDebtCommand>());
        var store = new InMemoryDebtStore();
        services.AddSingleton<IDebtRepository>(store);
        services.AddSingleton<IDebtReadStore>(store);
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task OpenDebt_ThenQuery_ReturnsDto()
    {
        var mediator = BuildMediator();

        var id = await mediator.Send(new OpenDebtCommand("К-42", 5000m));
        var dto = await mediator.Send(new GetDebtQuery(id));

        Assert.NotNull(dto);
        Assert.Equal(5000m, dto.Outstanding);
        Assert.False(dto.IsClosed);
    }

    [Fact]
    public async Task FullPaymentFlow_ClosesDebt()
    {
        var mediator = BuildMediator();
        var id = await mediator.Send(new OpenDebtCommand("К-43", 1000m));

        await mediator.Send(new RegisterPaymentCommand(id, 1000m));

        var dto = await mediator.Send(new GetDebtQuery(id));
        Assert.True(dto!.IsClosed);
        Assert.Equal(0m, dto.Outstanding);
    }
}
