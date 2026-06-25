// Mirrors the app's service subpackage split so test code resolves service types without a
// per-file `using` for each subpackage. See simple-bloomberg-terminal/GlobalUsings.cs.
global using simple_bloomberg_terminal.Services.ApiKeys;
global using simple_bloomberg_terminal.Services.Llm;
global using simple_bloomberg_terminal.Services.Clients.Edgar;
global using simple_bloomberg_terminal.Services.Clients.Fmp;
global using simple_bloomberg_terminal.Services.Clients.Yahoo;
global using simple_bloomberg_terminal.Services.Clients.Reference;
global using simple_bloomberg_terminal.Services.Clients.IndexSrc;
global using simple_bloomberg_terminal.Services.Extraction;
global using simple_bloomberg_terminal.Services.Discovery;
global using simple_bloomberg_terminal.Services.Provisioning;
global using simple_bloomberg_terminal.Services.Indices;
global using simple_bloomberg_terminal.Services.Classification;
global using simple_bloomberg_terminal.Services.Financials;
global using simple_bloomberg_terminal.Services.Impact;
global using simple_bloomberg_terminal.Services.Media;
