namespace Wpe.Core.Modules;

/// <summary>
/// Optional capability for rule variants that introduce their own attribute/variable names
/// (e.g. the counter variant contributes its units.json keys). The loader collects these
/// into the <see cref="NameContract"/> so expression names can be verified at load time.
/// </summary>
public interface INameContributor
{
    void ContributeNames(NameContract contract);
}
