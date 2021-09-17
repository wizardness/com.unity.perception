using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SemanticSegmentationBehaviour : MonoBehaviour
{
    //public bool enableTransparency = false;
    //public float opacityThreshold = 1;
    public bool useSegmentationMask;
    public Texture2D segmentationMask;
    public bool useMainTextureAsSegmask = false;
}
