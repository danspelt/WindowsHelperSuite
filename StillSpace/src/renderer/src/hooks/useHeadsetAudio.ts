import { useCallback, useEffect, useState } from 'react'
import {
  audioElementSupportsSetSinkId,
  ensureMicPermission,
  enumerateAudioDevices,
  resolveOpenRunOutput,
  subscribeDeviceChange,
  type AudioDevicePick
} from '../services/audio/audioDevices'
import { useSettingsStore } from '../stores/settingsStore'

export type HeadsetAudioState = {
  ready: boolean
  canRouteOutput: boolean
  headsetOutput: AudioDevicePick | null
  headsetConnected: boolean
  inputs: MediaDeviceInfo[]
  outputs: MediaDeviceInfo[]
  refresh: () => Promise<void>
}

export function useHeadsetAudio(): HeadsetAudioState {
  const savedOutputId = useSettingsStore((s) => s.preferredOutputDeviceId)
  const [ready, setReady] = useState(false)
  const [inputs, setInputs] = useState<MediaDeviceInfo[]>([])
  const [outputs, setOutputs] = useState<MediaDeviceInfo[]>([])
  const [headsetOutput, setHeadsetOutput] = useState<AudioDevicePick | null>(null)

  const refresh = useCallback(async () => {
    await ensureMicPermission()
    const { inputs: ins, outputs: outs } = await enumerateAudioDevices()
    setInputs(ins)
    setOutputs(outs)
    const resolved = resolveOpenRunOutput(outs, savedOutputId || undefined)
    setHeadsetOutput(resolved)
    setReady(true)
  }, [savedOutputId])

  useEffect(() => {
    void refresh()
  }, [refresh])

  useEffect(() => {
    return subscribeDeviceChange(() => {
      void refresh()
    })
  }, [refresh])

  const canRouteOutput = audioElementSupportsSetSinkId()

  return {
    ready,
    canRouteOutput,
    headsetOutput,
    headsetConnected: headsetOutput !== null,
    inputs,
    outputs,
    refresh
  }
}
