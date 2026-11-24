# CropScan XR presentation recording

**Presenter** Bhargav M, S20230030371  
**Evaluation** Third BTP evaluation, Dr. Pavan Kumar B N  
**My contribution** Unity VR experience and Quest demonstration  
**Target runtime** 9:20 including the demonstration. Keep your face visible in a corner for the entire recording. Keep the camera overlay clear of the slide text and demo controls.

## 0:00–0:25 · Title

Hi, I’m Bhargav M. Our project, CropScan XR, combines a leaf-photo scanner with a virtual farm and treatment lab. My teammate built the backend. I built the Unity VR experience, and I’ll show how learners inspect a plant, try a treatment and follow its recovery on Quest.

## 0:25–1:15 · Introduction

A leaf can look very different in a lab photo and a photo taken in the field. The lighting and background change, and different diseases can leave similar spots. Our team’s classifier benchmark used PlantVillage for the lab test and PlantDoc for the field test. The FAO estimates that plant pests and diseases reduce global crop yields by 20 to 40 percent each year. In the VR part I built, learners can look closely at the symptoms and practise what to do when the scanner finds a supported case.

## 1:15–2:15 · Literature review

Mohanty and colleagues studied leaf classification on PlantVillage images and checked whether it transfers to other images. PlantDoc and PlantWild provide benchmarks for photos taken outside a controlled setup. Our team’s classifier starts from BioCLIP, which learned visual features from a large collection of biological images. For the VR side, Cheng’s study of a virtual environment for teaching about plants gives context for letting learners handle a case and see the result of their choices.

## 2:15–3:00 · Quick recap

In my previous evaluation I presented the 360-degree VR Movie Player. For CropScan XR, my mentor wanted our team to connect leaf-photo identification with a lesson that shows how the plant is treated and how it recovers week by week. My teammate built the backend. I built the virtual farm and lab, the treatment chamber, and the six-week recovery timeline. My mentor was easy to reach and gave me the guidance and resources I needed.

## 3:00–4:15 · Methodology

The first part of the diagram is the team’s photo scanner. My teammate’s backend prepares the image and runs the classifier. A result appears with its confidence, or the scanner says it is unsure. A supported result can open the matching virtual specimen. My part is the Unity experience. Learners can find the specimen in the Farm, look more closely at its leaf in the Lab, and try a treatment. The assessment uses settings defined for that scenario and updates the simulated state. Unity can also launch an offline lesson without a photo upload.

## 4:15–6:25 · Implementation and demo

**4:15–4:45.** I built the VR experience in Unity using OpenXR and the XR Interaction Toolkit. The gallery shows all 19 plants in the Unity collection. The authored SQLite content contains 58 treatment cases. The connected flow maps 24 cases from the team’s scanner, which uses FastAPI and ONNX Runtime on the backend.

**4:45–5:10.** Show a web result or a known demo capture. Point out the predicted class and confidence. Briefly show how an uncertain image is presented if you have a ready example.

**5:10–5:40.** Switch to the Unity Farm. Select a tomato case and enter the Lab. Rotate the leaf to show a symptom, then point out the recovery timeline. Its values come from an illustrative scenario.

**5:40–6:15.** Open Treatment. Show the controls and explain what **Apply** and **Check Health** do. Show the result, then return to the Lab and step through the six-week timeline so the change in the plant is visible.

**6:15–6:25.** State the individual contributions: “I built and tested the Unity VR experience on Quest. My teammate built the backend that supplies the scanner’s predictions.”

## 6:25–8:00 · Results and analysis

The table shows our team’s backend classifier results. Its latest model scored 77.1 percent on 236 PlantDoc field images, up from 14.8 percent for the earlier lab-only model. It scored 99.4 percent on PlantVillage lab photos. These numbers describe the scanner. For my Unity VR work, all 167 selected Unity tests passed, and I confirmed that inspection, treatment and weekly recovery worked on Quest. The first screenshot shows the team’s web result. The second shows the Treatment Lab running in my Quest demo.

## 8:00–9:00 · Conclusion and future work

My part of CropScan XR gives learners a place to inspect symptoms, try a treatment and see how the plant changes week by week. I tested that experience on Quest. My mentor suggested two next steps. For VR assets, use RGB and infrared depth cameras to capture real plants and reconstruct them in 3D. For disease detection, use drone photos to spot signs of disease in a specific part of the farm and flag the area for a closer look. I’d also like to see whether practising in VR helps learners recognise symptoms and decide how to respond.

## 9:00–9:20 · References

These are the studies, datasets and tools I referred to. The project code and the full references are in the report. Thanks for watching.

## Submission fields

The portal asks for three separate viewer links: **Presentation Video + Results**, **Presentation file**, and **Project report**. Use the presentation file and the six-page report PDF. Test all three links in a signed-out or incognito window before confirming sharing. Use your own ratings, confirm the Third evaluation round, and save the displayed reference ID after the single submission.
