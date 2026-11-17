using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.BirBir;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Domtut;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Joymee;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Olx;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

/// <summary>Composes private marketplace sources over Harvest without subscribing them to the Ocharo event feed.</summary>
public static class MarketplaceServiceCollectionExtensions
{
    /// <summary>Registers marketplace readers, the listing delivery filter and the profile provisioner.</summary>
    public static IServiceCollection AddHarvestListingSources(this IServiceCollection services)
    {
        services.AddHarvestWebBroker<OlxSourceAdapter, OlxCaptureReader>();
        services.AddHarvestWebBroker<BirBirSourceAdapter, BirBirCaptureReader>();
        services.AddHarvestWebBroker<DomtutSourceAdapter, DomtutCaptureReader>();
        services.AddHarvestWebBroker<JoymeeSourceAdapter, JoymeeCaptureReader>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDeliveryPostFilter, MarketplaceListingFilter>());
        services.TryAddKeyedScoped<ICaptureProcessor, RentalTermsProcessor>(RentalTermsProcessor.ProcessorKey);
        services.TryAddScoped<MarketplaceProvisioner>();
        services.TryAddScoped<MarketplaceListingImporter>();
        return services;
    }
}
