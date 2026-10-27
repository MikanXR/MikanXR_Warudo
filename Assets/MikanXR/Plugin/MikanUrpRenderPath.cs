using System.Collections.Generic;
using MikanXR.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MikanXR.Warudo
{
	// The Mikan cameras under Warudo's URP mode. Camera.Render clears its target there and draws
	// nothing, so a frame renders from inside URP's own loop instead: requests made during the
	// frame queue up, and at the start of the next context render each one renders through
	// UniversalRenderPipeline.RenderSingleCamera. That runs before URP's per-frame setup, and the
	// lighting a render sets up still applies, since URP takes the ambient per object at culling.
	public class MikanUrpRenderPath : IMikanRenderPath
	{
		private struct RenderRequest
		{
			public MikanCameraObject cameraObject;
			public long frameIndex;
		}

		private readonly List<RenderRequest> _pending = new List<RenderRequest>();
		private readonly List<RenderRequest> _rendering = new List<RenderRequest>();
		private readonly Dictionary<Camera, DepthPackPass> _depthPacks = new Dictionary<Camera, DepthPackPass>();
		private ScriptableRenderContext _context;
		private bool _inContext;

		public MikanUrpRenderPath()
		{
			RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
		}

		public void Dispose()
		{
			RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
			_pending.Clear();
			_depthPacks.Clear();
		}

		public void Attach(MikanCameraObject cameraObject)
		{
			Configure(cameraObject.RenderCamera);
			Configure(cameraObject.ShadowCamera);
		}

		public void Detach(MikanCameraObject cameraObject)
		{
			_depthPacks.Remove(cameraObject.RenderCamera);
			_pending.RemoveAll(request => request.cameraObject == cameraObject);
		}

		// One request per camera waits at a time, the latest frame, so a frame URP never renders
		// (nothing else asked it to) cannot pile requests up
		public void RequestRender(MikanCameraObject cameraObject, long frameIndex)
		{
			var request = new RenderRequest() { cameraObject = cameraObject, frameIndex = frameIndex };
			int index = _pending.FindIndex(pending => pending.cameraObject == cameraObject);
			if (index >= 0)
			{
				_pending[index] = request;
			}
			else
			{
				_pending.Add(request);
			}
		}

		public void RenderCamera(Camera camera)
		{
			if (!_inContext)
			{
				return;
			}

			// RenderSingleCamera raises no beginCameraRendering, so the pass goes on the camera's
			// renderer here. URP empties that queue only once the camera has executed.
			if (_depthPacks.TryGetValue(camera, out DepthPackPass depthPack))
			{
				camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(depthPack);
			}

			UniversalRenderPipeline.RenderSingleCamera(_context, camera);
		}

		public void SetDepthPack(Camera camera, RenderTexture depthTarget, Material packMaterial)
		{
			if (camera == null)
			{
				return;
			}

			UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
			if (depthTarget == null)
			{
				_depthPacks.Remove(camera);
				cameraData.requiresDepthOption = CameraOverrideOption.Off;
				return;
			}

			cameraData.requiresDepthOption = CameraOverrideOption.On;
			_depthPacks[camera] = new DepthPackPass(depthTarget, packMaterial);
		}

		public void RenderUnshadowed(Camera camera)
		{
			UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
			cameraData.renderShadows = false;
			try
			{
				RenderCamera(camera);
			}
			finally
			{
				cameraData.renderShadows = true;
			}
		}

		// A Mikan camera renders the scene as it is, with none of Warudo's post-processing, and
		// takes the depth texture only while it packs depth
		private static void Configure(Camera camera)
		{
			UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
			cameraData.renderPostProcessing = false;
			cameraData.antialiasing = AntialiasingMode.None;
			cameraData.renderShadows = true;
			cameraData.requiresDepthOption = CameraOverrideOption.Off;
			cameraData.requiresColorOption = CameraOverrideOption.Off;
			cameraData.stopNaN = false;
			cameraData.dithering = false;
		}

		private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
		{
			if (_pending.Count == 0)
			{
				return;
			}

			_rendering.AddRange(_pending);
			_pending.Clear();

			_context = context;
			_inContext = true;
			try
			{
				foreach (RenderRequest request in _rendering)
				{
					if (request.cameraObject != null)
					{
						request.cameraObject.RenderNow(request.frameIndex);
					}
				}
			}
			finally
			{
				_inContext = false;
				_rendering.Clear();
			}
		}

		// Packs the camera depth texture into the Mikan depth target once the camera has rendered,
		// while that texture is still the one this camera produced. Warudo's SSAO makes URP build
		// that texture in a depth prepass, so it holds what has a DepthOnly or DepthNormals pass:
		// URP materials, but not the stencils' holdouts.
		private class DepthPackPass : ScriptableRenderPass
		{
			private readonly RenderTexture _target;
			private readonly Material _material;

			public DepthPackPass(RenderTexture target, Material material)
			{
				_target = target;
				_material = material;
				renderPassEvent = RenderPassEvent.AfterRendering;
			}

			public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
			{
				CommandBuffer commands = CommandBufferPool.Get("MikanXR pack depth");
				commands.Blit(BuiltinRenderTextureType.None, _target, _material);
				context.ExecuteCommandBuffer(commands);
				CommandBufferPool.Release(commands);
			}
		}
	}
}
