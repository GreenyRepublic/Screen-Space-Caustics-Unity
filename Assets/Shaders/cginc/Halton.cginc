#ifndef HALTON
#define HALTON

float halton( int base, int index) {
	float x = 0.0;
	float f = 1.0 / float(base);

	while(index != 0) {
		x += f * float(index % base);
		index /= base;
		f *= 1.0 / float(base);
	}
	return x;
}

int prime( int index) {
	int primes[11];
	primes[0] = 2;
	primes[1] = 3;
	primes[2] = 5;
	primes[3] = 7;
	primes[4] = 9;
	primes[5] = 11;
	primes[6] = 13;
	primes[7] = 17;
	primes[8] = 19;
	primes[9] = 23;
	primes[10] = 29;

	return primes[index % 10];
}

float2 halton2(int index) {
	return float2(
		halton(prime(0), index),
		halton(prime(1), index));
}

float3 halton3(int index) {
	return float3(
		halton(prime(0), index),
		halton(prime(1), index),
		halton(prime(2), index));
}

float4 halton4(int index) {
	return float4(
		halton(prime(0), index),
		halton(prime(1), index),
		halton(prime(2), index),
		halton(prime(3), index));
}

#endif