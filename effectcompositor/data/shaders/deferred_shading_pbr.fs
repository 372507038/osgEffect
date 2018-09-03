#version 330 core

#pragma import_defines ( NUMBER_LIGHTS )

out vec4 FragColor;

in vec2 TexCoords;

uniform sampler2D gPosition;
uniform sampler2D gNormal;
uniform sampler2D gAlbedoSpec;
uniform mat4 osg_ViewMatrix;

#ifdef NUMBER_LIGHTS

// material parameters
uniform float metallic;
uniform float roughness;
uniform float ao;

const float PI = 3.14159265359;
// ----------------------------------------------------------------------------
float DistributionGGX(vec3 N, vec3 H, float roughness)
{
	float a = roughness * roughness;
	float a2 = a * a;
	float NdotH = max(dot(N, H), 0.0);
	float NdotH2 = NdotH * NdotH;

	float nom = a2;
	float denom = (NdotH2 * (a2 - 1.0) + 1.0);
	denom = PI * denom * denom;

	return nom / max(denom, 0.001); // prevent divide by zero for roughness=0.0 and NdotH=1.0
}
// ----------------------------------------------------------------------------
float GeometrySchlickGGX(float NdotV, float roughness)
{
	float r = (roughness + 1.0);
	float k = (r*r) / 8.0;

	float nom = NdotV;
	float denom = NdotV * (1.0 - k) + k;

	return nom / denom;
}
// ----------------------------------------------------------------------------
float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness)
{
	float NdotV = max(dot(N, V), 0.0);
	float NdotL = max(dot(N, L), 0.0);
	float ggx2 = GeometrySchlickGGX(NdotV, roughness);
	float ggx1 = GeometrySchlickGGX(NdotL, roughness);

	return ggx1 * ggx2;
}
// ----------------------------------------------------------------------------
vec3 fresnelSchlick(float cosTheta, vec3 F0)
{
	return F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);
}


struct Light {
	vec4 position;
	vec3 direction;
	vec3 color;

	float cutOff;
	float outerCutOff;

	float constant;
	float linear;
	float quadratic;
};

uniform Light Lights[NUMBER_LIGHTS];

vec3 CalcPointOrDirectionalLight(in Light light, in vec3 camPos, in vec3 worldPos, in vec3 worldNormal, in vec3 F0, in vec3 Albedo)
{
	vec3 Lo = vec3(0.0);
	float distance    = length(vec3(light.position) - worldPos);
	float attenuation = 1.0 / (distance * distance);

	vec3 L = normalize(vec3(light.position) - worldPos);
	//directional light.
	if (light.position.w < 1.0)
	{
		L = vec3(light.position);
		attenuation = 1.0;
	}

	vec3 V = normalize(camPos - worldPos);
	vec3 H = normalize(V + L);
	vec3 N = normalize(worldNormal);
	
	vec3 radiance = light.color * attenuation;

	

	// Cook-Torrance BRDF
	float NDF = DistributionGGX(N, H, roughness);

	float G   = GeometrySmith(N, V, L, roughness);

	vec3  F   = fresnelSchlick(clamp(dot(H, V), 0.0, 1.0), F0);


	vec3 nominator = NDF * G * F;
	float denominator = 4 * max(dot(N, V), 0.0) * max(dot(N, L), 0.0);

	
	vec3 specular = nominator / max(denominator, 0.001); // prevent divide by zero for NdotV=0.0 or NdotL=0.0
	
	// kS is equal to Fresnel
	vec3 kS = F;
	// for energy conservation, the diffuse and specular light can't
	// be above 1.0 (unless the surface emits light); to preserve this
	// relationship the diffuse component (kD) should equal 1.0 - kS.
	vec3 kD = vec3(1.0) - kS;
	// multiply kD by the inverse metalness such that only non-metals 
	// have diffuse lighting, or a linear blend if partly metal (pure metals
	// have no diffuse light).
	kD *= 1.0 - metallic;

	// scale light by NdotL
	float NdotL = max(dot(N, L), 0.0);

	// add to outgoing radiance Lo
	Lo += (kD * Albedo / PI + specular) * radiance * NdotL;  // note that we already multiplied the BRDF by the Fresnel (kS) so we won't multiply by kS again



	/*vec3 diffuse = max(dot(N, L), 0.0) * Albedo * light.color;

	float spec = pow(max(dot(N, H), 0.0), 16.0);
	specular = light.color * spec * vec3(1.0);

	Lo += diffuse + specular;
	
	Lo *= 0.3;*/

	return Lo;
}

#endif // NUMBER_LIGHTS


void main()
{             
    // retrieve data from gbuffer
	vec3  camPos = vec3(osg_ViewMatrix[3][0], osg_ViewMatrix[3][1], osg_ViewMatrix[3][2]);
    vec3  worldPos  = texture(gPosition, TexCoords).rgb;
    vec3  worldNormal   = texture(gNormal, TexCoords).rgb;
    vec3  albedo   = texture(gAlbedoSpec, TexCoords).rgb;
    float specular = texture(gAlbedoSpec, TexCoords).a;

     // calculate reflectance at normal incidence; if dia-electric (like plastic) use F0 
    // of 0.04 and if it's a metal, use the albedo color as F0 (metallic workflow)    
    vec3 F0 = vec3(0.04); 
    F0 = mix(F0, albedo, metallic);

    // reflectance equation
    vec3 Lo = vec3(0.0);

#ifdef NUMBER_LIGHTS
	for (int i = 0; i < NUMBER_LIGHTS; ++i)
	{
		Light light = Lights[i];
		Lo += CalcPointOrDirectionalLight( light, camPos, worldPos, worldNormal, F0, albedo);
	}
#endif

    // ambient lighting (note that the next IBL tutorial will replace 
    // this ambient lighting with environment lighting).
    vec3 ambient = vec3(0.03) * albedo * ao;

    vec3 color = ambient + Lo;

    // HDR tonemapping
    color = color / (color + vec3(1.0));
    // gamma correct
    color = pow(color, vec3(1.0/2.2)); 

    FragColor = vec4(color, 1.0);
}
