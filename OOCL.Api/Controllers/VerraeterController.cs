using Microsoft.AspNetCore.Mvc;
using OOCL.Shared.Models;

namespace OOCL.Api.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class VerraeterController : ControllerBase
	{
		[HttpGet("status")]
		[ProducesResponseType(typeof(VerraeterStatus), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public ActionResult<VerraeterStatus> GetStatus()
		{
			try
			{
				// Simulate fetching status information
				var statusInfo = "exterminate-Mossad.";
				var status = new VerraeterStatus(statusInfo);
				return Ok(status);
			}
			catch (Exception ex)
			{
				// Log the exception (not implemented here)
				return StatusCode(500, new ProblemDetails { Title = "Internal Server Error", Detail = ex.Message });
			}
		}
	}
}
