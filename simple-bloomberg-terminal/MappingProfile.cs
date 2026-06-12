using AutoMapper;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal;

/// <summary>
/// AutoMapper entity &lt;-&gt; DTO maps. Response maps (Entity -&gt; Dto) hide internal
/// fields; request maps (RequestDto -&gt; Entity) feed the constructor + scalar props.
/// Nested DTO maps resolve automatically once each map below is registered.
/// </summary>
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        // Shallow reference used inside nested collections (Id + Name).
        CreateMap<Country, RelatedRefDto>();
        CreateMap<Company, RelatedRefDto>();
        CreateMap<TradeBloc, RelatedRefDto>();

        // Country
        CreateMap<Country, CountryDto>();
        CreateMap<CountryRequestDto, Country>();

        // Company (nested Country + source collections resolve via their maps)
        CreateMap<Company, CompanyDto>();
        CreateMap<CompanyRequestDto, Company>();

        // TradeBloc (nested Countries -> RelatedRefDto). Join membership is applied by
        // the repository, so the nav collection is left untouched by the mapper.
        CreateMap<TradeBloc, TradeBlocDto>();
        CreateMap<TradeBlocRequestDto, TradeBloc>()
            .ForMember(t => t.Countries, o => o.Ignore());

        // CountryDetails
        CreateMap<CountryDetails, CountryDetailsDto>();
        CreateMap<CountryDetailsRequestDto, CountryDetails>();

        // CountryAdvantage
        CreateMap<CountryAdvantage, CountryAdvantageDto>();
        CreateMap<CountryAdvantageRequestDto, CountryAdvantage>();

        // CountryChallenge
        CreateMap<CountryChallenge, CountryChallengeDto>();
        CreateMap<CountryChallengeRequestDto, CountryChallenge>();

        // GdpSnapshot
        CreateMap<GdpSnapshot, GdpSnapshotDto>();
        CreateMap<GdpSnapshotRequestDto, GdpSnapshot>();

        // RevenueSource
        CreateMap<RevenueSource, RevenueSourceDto>();
        CreateMap<RevenueSourceRequestDto, RevenueSource>();

        // CostSource
        CreateMap<CostSource, CostSourceDto>();
        CreateMap<CostSourceRequestDto, CostSource>();

        // Event: M:N collections surface as nested RelatedRefDto lists (resolve via the
        // RelatedRefDto maps above). On the write side the *Ids arrays are extra source
        // members AutoMapper ignores; the nav collections are explicitly ignored because
        // the repository overloads apply join membership from the id lists.
        CreateMap<Event, EventDto>();
        CreateMap<EventRequestDto, Event>()
            .ForMember(e => e.Countries, o => o.Ignore())
            .ForMember(e => e.Companies, o => o.Ignore())
            .ForMember(e => e.TradeBlocs, o => o.Ignore());

        // CompanyRisk
        CreateMap<CompanyRisk, CompanyRiskDto>();
        CreateMap<CompanyRiskRequestDto, CompanyRisk>();

        // CompanyFinancial
        CreateMap<CompanyFinancial, CompanyFinancialDto>();
        CreateMap<CompanyFinancialRequestDto, CompanyFinancial>();

        // Filing: Create goes through repo.Upsert (accession-keyed), so only the Update
        // direction is needed here.
        CreateMap<Filing, FilingDto>();
        CreateMap<FilingRequestDto, Filing>();

        // SourceFieldReview
        CreateMap<SourceFieldReview, SourceFieldReviewDto>();
        CreateMap<SourceFieldReviewRequestDto, SourceFieldReview>();

        // Scenario (nested Shocks -> ScenarioShockDto, resolves via the ScenarioShock map below)
        CreateMap<Scenario, ScenarioDto>();
        CreateMap<ScenarioRequestDto, Scenario>();

        // ScenarioShock
        CreateMap<ScenarioShock, ScenarioShockDto>();
        CreateMap<ScenarioShockRequestDto, ScenarioShock>();

        // Graph: Company -> hub-and-spoke GraphResponse. Imperative node/edge build (1->N
        // expansion + counterparty dedup), so it uses a custom converter instead of member
        // mapping. Shared by the MVC GraphController (HTML) and API GraphController (JSON).
        CreateMap<Company, GraphResponse>().ConvertUsing<CompanyGraphConverter>();
    }
}
