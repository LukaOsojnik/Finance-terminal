using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public interface ITradeBlocRepository
{
    IEnumerable<TradeBloc> GetAll();
    TradeBloc? GetById(long id);
    IEnumerable<TradeBloc> Search(string? term);
    void Add(TradeBloc entity, IEnumerable<long> countryIds);
    void Update(TradeBloc entity, IEnumerable<long> countryIds);
    void SoftDelete(long id);
}
