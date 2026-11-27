#ifndef STABBY_CLOCK
#define STABBY_CLOCK

float4 _StabbyTimePin;

#define StabbyTime (_StabbyTimePin.x > 0 ? _StabbyTimePin.y : _Time.y)

#endif
