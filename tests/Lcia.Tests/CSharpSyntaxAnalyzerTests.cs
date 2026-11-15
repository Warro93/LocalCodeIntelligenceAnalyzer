using Lcia.Analysis.CSharp;
using Lcia.Application;
using Lcia.Domain;

namespace Lcia.Tests;

public sealed class CSharpSyntaxAnalyzerTests
{
    private const string Source = """
        using System;
        using Demo.Data;

        namespace Demo.Services;

        /// <summary>Servizio di prova per <see cref="T:Demo.Order"/>.</summary>
        [Obsolete("test")]
        public sealed class OrderService : IOrderService, IDisposable
        {
            private readonly IOrderRepository _repository;
            private readonly ILogger<OrderService> _logger;
            private int _count;

            public OrderService(IOrderRepository repository, ILogger<OrderService> logger)
            {
                _repository = repository;
                _logger = logger;
            }

            public string Name { get; set; } = "";

            /// <summary>Crea un ordine.</summary>
            public async Task<int> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken)
            {
                var id = await _repository.InsertOrderAsync(request, cancellationToken);
                _logger.LogInformation("ok");
                return id;
            }

            void IDisposable.Dispose()
            {
            }

            public sealed record Line(string Code, int Quantity);
        }

        public interface IOrderService
        {
            Task<int> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken);
        }

        public enum Status
        {
            Created = 0,
            Cancelled = 2,
        }

        public sealed class OrdersController(IOrderService service)
        {
        }

        public static class Startup
        {
            public static void Configure(IServiceCollection services)
            {
                services.AddScoped<IOrderService, OrderService>();
                services.AddSingleton(typeof(IClock), typeof(SystemClock));
            }
        }
        """;

    private static SourceAnalysisResult Analyze(string source = Source) =>
        new CSharpSyntaxAnalyzer().Analyze("Services/OrderService.cs", source, CancellationToken.None);

    [Fact]
    public void ExtractsTypesWithNamespaceLinesAndDocumentation()
    {
        var result = Analyze();
        Assert.Equal(SupportLevel.Syntactic, result.Level);
        Assert.Empty(result.Diagnostics);

        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Namespace && s.FullName == "Demo.Services");
        var service = Assert.Single(result.Symbols, s => s.Kind == SymbolKind.Class && s.Name == "OrderService");
        Assert.Equal("Demo.Services.OrderService", service.FullName);
        Assert.Equal("Demo.Services", service.Namespace);
        Assert.Equal("public", service.Accessibility);
        Assert.Contains("sealed", service.Modifiers);
        Assert.Contains("Obsolete", service.Attributes);
        Assert.Equal(SampleRepository.LineOf(Source, "[Obsolete(\"test\")]"), service.StartLine);
        Assert.Equal(SampleRepository.LineOf(Source, "public interface IOrderService") - 2, service.EndLine);
        Assert.NotNull(service.Documentation);
        Assert.Contains("Servizio di prova", service.Documentation);
        Assert.Contains("Demo.Order", service.Documentation);

        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Interface && s.FullName == "Demo.Services.IOrderService");
        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Enum && s.Name == "Status");
        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.EnumMember && s.FullName == "Demo.Services.Status.Cancelled");
        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Record && s.FullName == "Demo.Services.OrderService.Line");
    }

    [Fact]
    public void ExtractsMembersWithSignaturesAndParents()
    {
        var result = Analyze();
        var symbols = result.Symbols;
        var serviceIndex = symbols.ToList().FindIndex(s => s.Kind == SymbolKind.Class && s.Name == "OrderService");

        var method = Assert.Single(symbols, s => s.Kind == SymbolKind.Method && s.FullName == "Demo.Services.OrderService.CreateOrderAsync");
        Assert.Equal(SampleRepository.LineOf(Source, "public async Task<int> CreateOrderAsync"), method.StartLine);
        Assert.Equal(serviceIndex, method.ParentIndex);
        Assert.Contains("Task<int> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken)", method.Signature);
        Assert.Equal("async", method.Modifiers);
        Assert.Equal("Crea un ordine.", method.Documentation);

        Assert.Contains(symbols, s => s.Kind == SymbolKind.Constructor && s.FullName == "Demo.Services.OrderService.OrderService");
        Assert.Contains(symbols, s => s.Kind == SymbolKind.Method && s.Name == "IDisposable.Dispose");
        Assert.Contains(symbols, s => s.Kind == SymbolKind.Field && s.Name == "_repository" && s.Accessibility == "private");
        Assert.Contains(symbols, s => s.Kind == SymbolKind.Property && s.Name == "Name" && s.Signature!.Contains("{ get; set; }"));

        var interfaceMethod = Assert.Single(symbols, s => s.Kind == SymbolKind.Method && s.FullName == "Demo.Services.IOrderService.CreateOrderAsync");
        Assert.Equal("public", interfaceMethod.Accessibility);
    }

    [Fact]
    public void ExtractsSyntacticRelationsWithEvidence()
    {
        var relations = Analyze().Relations;

        Assert.Contains(relations, r => r.Kind == RelationKind.BaseType && r.SourceName == "Demo.Services.OrderService" && r.TargetName == "IOrderService");
        Assert.Contains(relations, r => r.Kind == RelationKind.BaseType && r.TargetName == "IDisposable");
        Assert.Contains(relations, r => r.Kind == RelationKind.ConstructorInjection && r.TargetName == "IOrderRepository"
                                        && r.Evidence == EvidenceLevel.InferredWithEvidence);
        Assert.Contains(relations, r => r.Kind == RelationKind.ConstructorInjection && r.TargetName == "ILogger<OrderService>" && r.TargetSimpleName == "ILogger");
        Assert.Contains(relations, r => r.Kind == RelationKind.MemberType && r.TargetName == "IOrderRepository");
        Assert.DoesNotContain(relations, r => r.Kind == RelationKind.MemberType && r.TargetName == "int");
        Assert.Contains(relations, r => r.Kind == RelationKind.ConstructorInjection && r.SourceName == "Demo.Services.OrdersController" && r.TargetName == "IOrderService");

        var invocation = Assert.Single(relations, r => r.Kind == RelationKind.Invocation && r.TargetSimpleName == "InsertOrderAsync");
        Assert.Equal("_repository.InsertOrderAsync", invocation.TargetName);
        Assert.Equal(EvidenceLevel.TextualReference, invocation.Evidence);
        Assert.Equal(SampleRepository.LineOf(Source, "await _repository.InsertOrderAsync"), invocation.Line);
        Assert.Equal("Demo.Services.OrderService.CreateOrderAsync", invocation.SourceName);

        Assert.Contains(relations, r => r.Kind == RelationKind.DiRegistration && r.SourceName == "IOrderService" && r.TargetName == "OrderService");
        Assert.Contains(relations, r => r.Kind == RelationKind.DiRegistration && r.SourceName == "IClock" && r.TargetName == "SystemClock");
        Assert.Contains(relations, r => r.Kind == RelationKind.UsingNamespace && r.TargetName == "Demo.Data");
    }

    [Fact]
    public void ToleratesSyntaxErrors()
    {
        var result = Analyze("namespace Broken;\npublic class A {\n  public void M( {\n}\n");
        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Class && s.Name == "A");
    }

    [Fact]
    public void FactoryRegistrationsAreMarkedDynamic()
    {
        var result = Analyze("class S { void C(IServiceCollection s) { s.AddScoped<IClock>(sp => new SystemClock()); } }");
        var registration = Assert.Single(result.Relations, r => r.Kind == RelationKind.DiRegistration);
        Assert.Equal(EvidenceLevel.Dynamic, registration.Evidence);
    }
}
