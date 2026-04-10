export function RoomScene() {
  return (
    <>
      <color attach="background" args={['#12161f']} />
      <fog attach="fog" args={['#12161f', 8, 22]} />
      <ambientLight intensity={0.35} />
      <directionalLight position={[4, 8, 4]} intensity={0.7} castShadow />
      <pointLight position={[-3, 2.2, 2]} intensity={0.45} color="#c8b8a8" />
      <mesh rotation-x={-Math.PI / 2} position={[0, -0.02, 0]} receiveShadow>
        <planeGeometry args={[24, 24]} />
        <meshStandardMaterial color="#1e2430" roughness={0.9} metalness={0.05} />
      </mesh>
      <mesh position={[0, 1.4, -3.6]} receiveShadow>
        <planeGeometry args={[10, 3.2]} />
        <meshStandardMaterial color="#252b38" roughness={0.95} />
      </mesh>
      <mesh position={[-3.2, 1.1, -1]} castShadow>
        <boxGeometry args={[0.08, 2.2, 0.08]} />
        <meshStandardMaterial color="#3a3228" />
      </mesh>
      <mesh position={[-3.2, 2.25, -1]}>
        <sphereGeometry args={[0.22, 24, 24]} />
        <meshStandardMaterial emissive="#d4c4a8" emissiveIntensity={0.9} color="#2a2520" />
      </mesh>
    </>
  )
}
