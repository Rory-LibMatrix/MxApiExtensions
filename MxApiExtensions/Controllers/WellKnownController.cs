using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;

namespace MxApiExtensions.Controllers;

[ApiController]
[Route("/")]
public class WellKnownController : ControllerBase {
    private readonly MxApiExtensionsConfiguration _config;

    public WellKnownController(MxApiExtensionsConfiguration config) {
        _config = config;
    }

    [HttpGet("/.well-known/matrix/client")]
    public object GetWellKnown() {
        var res = new JsonObject();
        res.Add("m.homeserver", new JsonObject {
            { "base_url", Request.Scheme + "://" + Request.Host + "/" },
        });
        return res;
    }
}
