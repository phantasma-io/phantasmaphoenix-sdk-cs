using System.Net;

namespace PhantasmaPhoenix.RPC.Types;

public class RpcError
{
	public RpcError(int code, string message)
	{
		Code = code;
		Message = message;
	}

	public RpcError(RpcErrorDescription rpcErrorDescription)
	{
		Code = rpcErrorDescription.Code;
		Message = rpcErrorDescription.Message;
	}

	public RpcError(RpcErrorDescription rpcErrorDescription, string additionalErrorInfo)
	{
		Code = rpcErrorDescription.Code;
		Message = rpcErrorDescription.Message + additionalErrorInfo;
	}

	public int Code { get; set; }
	public string Message { get; set; }
}

public class RpcErrorDescription
{
	public int Code { get; set; }
	public string Message { get; set; }
	public HttpStatusCode HttpCode { get; set; }

	public RpcErrorDescription(int code, string message, HttpStatusCode httpCode)
	{
		Code = code;
		Message = message;
		HttpCode = httpCode;
	}
}

public static class RpcErrors
{
	public const int RPC_ERROR_PARSE = -32700;
	public const int RPC_ERROR_INVALID = -32600;
	public const int RPC_ERROR_METHOD = -32601;
	public const int RPC_ERROR_PARAMS = -32602;
	public const int RPC_ERROR_INTERNAL = -32603;
	public const int RPC_ERROR_IMPLEMENTATION = -32000;

	public static Dictionary<int, RpcErrorDescription> errors = new()
	{
		{ RPC_ERROR_PARSE, new RpcErrorDescription(RPC_ERROR_PARSE, "Parse error", HttpStatusCode.BadRequest) },
		{ RPC_ERROR_INVALID, new RpcErrorDescription(RPC_ERROR_INVALID, "Invalid request", HttpStatusCode.BadRequest) },
		{ RPC_ERROR_METHOD, new RpcErrorDescription(RPC_ERROR_METHOD, "Method not found", HttpStatusCode.BadRequest) },
		{ RPC_ERROR_PARAMS, new RpcErrorDescription(RPC_ERROR_PARAMS, "Invalid params", HttpStatusCode.BadRequest) },
		{ RPC_ERROR_INTERNAL, new RpcErrorDescription(RPC_ERROR_INTERNAL, "Internal error", HttpStatusCode.BadRequest) },
		{ RPC_ERROR_IMPLEMENTATION, new RpcErrorDescription(RPC_ERROR_IMPLEMENTATION, "Implementation error", HttpStatusCode.BadRequest) }
	};

	public static RpcErrorDescription GetDescription(int code)
	{
		return errors[code];
	}
}
