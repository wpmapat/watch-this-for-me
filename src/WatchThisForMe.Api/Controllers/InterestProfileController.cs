using Microsoft.AspNetCore.Mvc;
using WatchThisForMe.Api.Contracts;
using WatchThisForMe.Core.Preferences;

namespace WatchThisForMe.Api.Controllers;

[ApiController]
[Route("api/interest-profile")]
public class InterestProfileController(IInterestProfileRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<InterestProfile>> Get(CancellationToken cancellationToken)
    {
        var profile = await repository.GetAsync(cancellationToken);
        return Ok(profile);
    }

    [HttpPut]
    public async Task<ActionResult<InterestProfile>> Update(UpdateInterestProfileRequest request, CancellationToken cancellationToken)
    {
        var profile = await repository.GetAsync(cancellationToken);
        profile.Interests = request.Interests;

        var updated = await repository.UpdateAsync(profile, cancellationToken);
        return Ok(updated);
    }
}
