using System.Reflection;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Qonaqet.ArchitectureTests.Support;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.ArchitectureTests;

/// <summary>Controller-lərin incəliyi, adlandırma və layihə qrafiki qaydaları (E01-T14).</summary>
public sealed class ConventionTests
{
    private static readonly Assembly[] ProductionAssemblies =
    [
        typeof(SharedKernel.SharedKernelAssembly).Assembly,
        typeof(Domain.DomainAssembly).Assembly,
        typeof(Contracts.ContractsAssembly).Assembly,
        typeof(Application.ApplicationAssembly).Assembly,
        typeof(Infrastructure.DependencyInjection).Assembly,
        typeof(Api.Program).Assembly,
        typeof(Integration.Api.Program).Assembly,
        typeof(Worker.Program).Assembly,
    ];

    private static IEnumerable<Type> ProductionTypes => ProductionAssemblies.SelectMany(a => a.GetTypes());

    // ---------- controller-lər ----------

    [Fact]
    public void Controllers_depend_only_on_the_sender()
    {
        Assert.Empty(Rules.ControllerViolations(ProductionTypes));
    }

    [Fact]
    public void Partner_controllers_live_under_the_partner_prefix_or_oauth()
    {
        var routes = typeof(Integration.Api.Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Select(t => (t.Name, Route: t.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty))
            .ToList();

        Assert.NotEmpty(routes);
        Assert.All(routes, r => Assert.True(
            r.Route == Integration.Api.Controllers.PartnerRoutes.Prefix
                || r.Route.StartsWith(Integration.Api.Controllers.PartnerRoutes.Prefix + "/", StringComparison.Ordinal)
                || r.Route.StartsWith(Integration.Api.Controllers.PartnerRoutes.OAuth + "/", StringComparison.Ordinal),
            $"{r.Name}: {r.Route}"));
    }

    [Fact]
    public void Controller_rule_catches_injected_persistence_and_state()
    {
        var violations = Rules.ControllerViolations([typeof(FatController), typeof(ThinController)]);

        Assert.Contains(violations, v => v.Contains(nameof(FatController), StringComparison.Ordinal) && v.Contains("FakeDbContext", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains(nameof(FatController), StringComparison.Ordinal) && v.Contains("_cache", StringComparison.Ordinal));
        Assert.DoesNotContain(violations, v => v.Contains(nameof(ThinController), StringComparison.Ordinal));
    }

    [Fact]
    public void Controllers_leave_authorization_to_the_pipeline()
    {
        var authAttributes = new[] { typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute) };
        var violations = ProductionTypes
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => new MemberInfo[] { t }.Concat(t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)))
            .Where(m => authAttributes.Any(a => m.IsDefined(a, inherit: true)))
            .Select(m => m is Type type ? type.Name : $"{m.DeclaringType!.Name}.{m.Name}")
            .ToArray();

        Assert.True(violations.Length == 0, $"Siyasət controller-də deyil, sorğuda olmalıdır ([RequiresRole]/[PublicRequest]): {string.Join(", ", violations)}");
    }

    // ---------- adlandırma ----------

    [Fact]
    public void Requests_handlers_and_validators_follow_naming_conventions()
    {
        Assert.Empty(Rules.NamingViolations(ProductionTypes));
    }

    [Fact]
    public void Naming_rule_catches_badly_named_types()
    {
        var violations = Rules.NamingViolations([typeof(CreateGift), typeof(CreateGiftCommand), typeof(GiftLookup), typeof(GiftsQuery), typeof(GiftsQueryHandler), typeof(GiftChecks)]);

        Assert.Equal(
            [nameof(CreateGift), nameof(GiftChecks), nameof(GiftLookup)],
            violations.Select(v => v.Split(':')[0]).Order(StringComparer.Ordinal));
    }

    // ---------- layihə qrafiki (csproj) ----------

    [Fact]
    public void Project_references_follow_the_layer_direction()
    {
        Assert.Empty(Rules.ProjectReferenceViolations(RepoPaths.ProjectFiles().Select(p => (Path.GetFileNameWithoutExtension(p), XDocument.Load(p)))));
    }

    [Fact]
    public void Project_reference_rule_catches_an_inverted_reference()
    {
        var domain = XDocument.Parse("<Project><ItemGroup><ProjectReference Include=\"../Qonaqet.Infrastructure/Qonaqet.Infrastructure.csproj\" /><PackageReference Include=\"MongoDB.Driver\" /></ItemGroup></Project>");

        var violations = Rules.ProjectReferenceViolations([("Qonaqet.Domain", domain)]);

        Assert.Contains(violations, v => v.Contains("Qonaqet.Infrastructure", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("MongoDB.Driver", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_does_not_use_aspnetcore()
    {
        var aspnet = typeof(Application.ApplicationAssembly).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
            .ToArray();

        Assert.True(aspnet.Length == 0, $"Application ASP.NET Core-a istinad edir: {string.Join(", ", aspnet)}");
    }

    // ---------- qaydalar ----------

    internal static class Rules
    {
        private static readonly Dictionary<string, string[]> AllowedProjectReferences = new()
        {
            ["Qonaqet.SharedKernel"] = [],
            ["Qonaqet.Contracts"] = [],
            ["Qonaqet.Domain"] = ["Qonaqet.SharedKernel"],
            ["Qonaqet.Application"] = ["Qonaqet.Domain", "Qonaqet.Contracts"],
            ["Qonaqet.Infrastructure"] = ["Qonaqet.Application"],
            ["Qonaqet.Api"] = ["Qonaqet.Infrastructure"],
            ["Qonaqet.Integration.Api"] = ["Qonaqet.Infrastructure"],
            ["Qonaqet.Worker"] = ["Qonaqet.Infrastructure"],
        };

        private static readonly HashSet<string> ProjectsWithoutPackages = ["Qonaqet.SharedKernel", "Qonaqet.Contracts", "Qonaqet.Domain", "Qonaqet.Application"];

        /// <summary>Controller yalnız <see cref="ISender"/> (və logger) alır və vəziyyət saxlamır — məntiq handler-dədir.</summary>
        public static List<string> ControllerViolations(IEnumerable<Type> types)
        {
            var violations = new List<string>();
            foreach (var controller in types.Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
            {
                foreach (var parameter in controller.GetConstructors().SelectMany(c => c.GetParameters()))
                {
                    var type = parameter.ParameterType;
                    var allowed = type == typeof(ISender)
                        || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Logging.ILogger<>));
                    if (!allowed)
                    {
                        violations.Add($"{controller.Name}: konstruktor {type.Name} alır (yalnız ISender icazəlidir)");
                    }
                }

                foreach (var field in controller.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                             .Where(f => f.FieldType != typeof(ISender) && !f.Name.Contains("ogger", StringComparison.Ordinal)))
                {
                    violations.Add($"{controller.Name}: sahə {field.Name} ({field.FieldType.Name}) — controller vəziyyət saxlamır");
                }
            }

            return violations;
        }

        public static List<string> NamingViolations(IEnumerable<Type> types)
        {
            var violations = new List<string>();
            foreach (var type in types.Where(t => t is { IsAbstract: false, IsInterface: false } && !t.IsGenericTypeDefinition))
            {
                var interfaces = type.GetInterfaces().Where(i => i.IsGenericType).Select(i => i.GetGenericTypeDefinition()).ToHashSet();
                var name = type.Name;

                if (typeof(ICommandBase).IsAssignableFrom(type) && !name.EndsWith("Command", StringComparison.Ordinal))
                {
                    violations.Add($"{name}: əmr adı 'Command' ilə bitməlidir");
                }

                if (typeof(IQueryBase).IsAssignableFrom(type) && !name.EndsWith("Query", StringComparison.Ordinal))
                {
                    violations.Add($"{name}: sorğu adı 'Query' ilə bitməlidir");
                }

                if (interfaces.Contains(typeof(IRequestHandler<,>)) && !name.EndsWith("Handler", StringComparison.Ordinal))
                {
                    violations.Add($"{name}: handler adı 'Handler' ilə bitməlidir");
                }

                if (interfaces.Contains(typeof(IValidator<>)) && !name.EndsWith("Validator", StringComparison.Ordinal))
                {
                    violations.Add($"{name}: validator adı 'Validator' ilə bitməlidir");
                }
            }

            return violations;
        }

        public static List<string> ProjectReferenceViolations(IEnumerable<(string Project, XDocument Document)> projects)
        {
            var violations = new List<string>();
            foreach (var (project, document) in projects)
            {
                if (!AllowedProjectReferences.TryGetValue(project, out var allowed))
                {
                    violations.Add($"{project}: tanınmayan layihə — CLAUDE.md 'Backend structure'-a əlavə edin");
                    continue;
                }

                var references = document.Descendants("ProjectReference")
                    .Select(r => Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value.Replace('\\', '/')))
                    .ToArray();
                violations.AddRange(references.Except(allowed).Select(r => $"{project} → {r} icazəli deyil"));

                if (ProjectsWithoutPackages.Contains(project))
                {
                    violations.AddRange(document.Descendants("PackageReference")
                        .Select(p => $"{project}: NuGet paketi {p.Attribute("Include")?.Value} qadağandır"));
                }
            }

            return violations;
        }
    }

    // ---------- qaydaları sınamaq üçün nümunələr ----------

    private sealed class FakeDbContext;

    [ApiController]
    private sealed class FatController(FakeDbContext db) : ControllerBase
    {
        private readonly Dictionary<string, object> _cache = [];

        public int Count() => db.GetHashCode() + _cache.Count;
    }

    [ApiController]
    private sealed class ThinController(ISender sender) : ControllerBase
    {
        public Task<SharedKernel.Result> Ping() => sender.Send(new PingCommand());
    }

    private sealed record PingCommand : ICommand;

    private sealed record CreateGift : ICommand;

    private sealed record CreateGiftCommand : ICommand;

    private sealed record GiftLookup : IQuery<int>;

    private sealed record GiftsQuery : IQuery<int>;

    private sealed class GiftsQueryHandler : IQueryHandler<GiftsQuery, int>
    {
        public Task<SharedKernel.Result<int>> Handle(GiftsQuery request, CancellationToken cancellationToken) => Task.FromResult(SharedKernel.Result<int>.Success(1));
    }

    private sealed class GiftChecks : Validator<CreateGiftCommand>;
}
