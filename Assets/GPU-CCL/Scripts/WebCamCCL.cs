using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WebCamCCL : MonoBehaviour {

    public int width = 640;
    public int height = 480;
    public Material visualizer;
    public CCL ccl;
    WebCamTexture webcamTex;

	// Use this for initialization
	void Start () {
        webcamTex = new WebCamTexture(width, height);
        webcamTex.Play();
	}

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        ccl.Compute(webcamTex);
        ccl.BuildBlobs();
        visualizer.SetTexture("_LabelTex", ccl.output);
        Graphics.Blit(webcamTex, destination, visualizer);
    }

    private void OnGUI()
    {
        for (var i = 0; i < ccl.numBlobs; i++)
        {
            var blob = ccl.blobs[i];
            var content = string.Format("{0}_{1}", i, blob.center);
            blob.x *= Screen.width;
            blob.y = (1f - blob.y - blob.height) * Screen.height;
            blob.width *= Screen.width;
            blob.height *= Screen.height;

            GUI.Box(blob, content, "box");
        }
    }
}
