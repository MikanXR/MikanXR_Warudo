using System;
using System.Threading.Tasks;
using UnityEngine;

namespace MikanXR
{
	public class WriteUnityColorRenderTargetTexture : WriteColorRenderTargetTexture
	{
		public RenderTexture colorTexture;
	};

	public class WriteUnityDepthRenderTargetTexture : WriteDepthRenderTargetTexture
	{
		public RenderTexture depthTexture;
	};

	public class MikanRenderTargetAPI
	{
		private MikanRequestManager _requestManager;
		private MikanCoreAPI _coreAPI;

		public MikanRenderTargetAPI(MikanRequestManager requestManager)
		{
			_requestManager = requestManager;
			_coreAPI= requestManager.CoreAPI;
		}

		public MikanAPIResult Initialize()
		{
			return MikanAPIResult.Success;
		}

		//public MikanAPIResult SetGraphicsDeviceInterface(
		//	MikanClientGraphicsApi api,
		//	IntPtr graphicsDeviceInterface)
		//{
		//	int result = 
		//		MikanCoreNative.Mikan_SetGraphicsDeviceInterface(
		//			_mikanContext, api, graphicsDeviceInterface);
		//	return (MikanAPIResult)result;
		//}

		//public MikanAPIResult GetGraphicsDeviceInterface(
		//	MikanClientGraphicsApi api,
		//	out IntPtr outGraphicsDeviceInterface)
		//{
		//	int result = 
		//		MikanCoreNative.Mikan_GetGraphicsDeviceInterface(
		//			_mikanContext, api, out outGraphicsDeviceInterface);
		//	return (MikanAPIResult)result;
		//}

		//public IntPtr GetPackDepthTextureResourcePtr()
		//{
		//	return MikanCoreNative.Mikan_GetPackDepthTextureResourcePtr(_mikanContext);
		//}

		public MikanResponseFuture TryProcessRequest(MikanRequest request)
		{
			if (request is AllocateRenderTargetTextures)
			{
				return RequestAllocateRenderTargetTextures(request);
			}
			else if (request is WriteUnityColorRenderTargetTexture)
			{
				return RequestWriteColorRenderTargetTexture(request);
			}
			else if (request is WriteUnityDepthRenderTargetTexture)
			{
				return RequestWriteDepthRenderTargetTexture(request);
			}
			else if (request is PublishRenderTargetTextures) 
			{
				return RequestPublishRenderTargetTextures(request);
			}
			else if (request is FreeRenderTargetTextures)
			{
				return RequestFreeRenderTargetTextures(request);
			}

			return new MikanResponseFuture();
		}

		private MikanResponseFuture RequestAllocateRenderTargetTextures(MikanRequest request)
		{
			var allocateRequest = request as AllocateRenderTargetTextures;
			MikanRenderTargetDescriptor descriptor= allocateRequest.descriptor;

			MikanAPIResult result = _coreAPI.AllocateRenderTargetTextures(descriptor);
			if (result == MikanAPIResult.Success)
			{
				// Actual descriptor might differ from desired descriptor based on render target writer's capabilities
				MikanRenderTargetDescriptor actualDescriptor;
				result= _coreAPI.GetRenderTargetDescriptor(out actualDescriptor);
				if (result == MikanAPIResult.Success)
				{
					return _requestManager.SendRequest(allocateRequest);
				}
			}

			return _requestManager.AddResponseHandler(-1, MikanAPIResult.RequestFailed);
		}

		private MikanResponseFuture RequestWriteColorRenderTargetTexture(MikanRequest request)
		{
			var writeRequest = request as WriteUnityColorRenderTargetTexture;
			MikanAPIResult result = _coreAPI.WriteColorRenderTargetTexture(writeRequest.colorTexture);

			return new MikanResponseFuture(result);
		}

		private MikanResponseFuture RequestWriteDepthRenderTargetTexture(MikanRequest request)
		{
			var writeRequest = request as WriteUnityDepthRenderTargetTexture;
			RenderTexture depthTexture= writeRequest.depthTexture;
			float zNear= writeRequest.zNear;
			float zFar= writeRequest.zFar;

			MikanAPIResult result = _coreAPI.WriteDepthRenderTargetTexture(depthTexture, zNear, zFar);

			return new MikanResponseFuture(result);
		}

		private MikanResponseFuture RequestPublishRenderTargetTextures(MikanRequest request)
		{
			return _requestManager.SendRequest(request);
		}

		private MikanResponseFuture RequestFreeRenderTargetTextures(MikanRequest request)
		{
			// Free any locally allocated resources
			_coreAPI.FreeRenderTargetTextures();

			// Tell the server to free the render target resources too
			return _requestManager.SendRequest(request);
		}
	}
}